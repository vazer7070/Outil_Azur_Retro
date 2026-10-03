//! Exporte des symboles (formes, clips, boutons) d'un SWF en SVG.
//! usage : swfsvg <fichier.swf> <dossier> [nomExport ...]   (sans nom : tous les exports)
use base64::Engine;
use std::collections::{BTreeMap, HashMap};
use std::fmt::Write as _;
use std::fs::File;
use std::io::{Read, Write};
use swf::{FillStyle, Matrix, ShapeRecord, Tag};

const ENC: &'static encoding_rs::Encoding = encoding_rs::UTF_8;

type Px = f64;

#[derive(Clone, Copy, Debug)]
struct M {
    a: f64,
    b: f64,
    c: f64,
    d: f64,
    tx: f64,
    ty: f64,
}

impl M {
    fn identity() -> M {
        M { a: 1.0, b: 0.0, c: 0.0, d: 1.0, tx: 0.0, ty: 0.0 }
    }
    fn from_swf(m: &Matrix) -> M {
        M {
            a: m.a.to_f32() as f64,
            b: m.b.to_f32() as f64,
            c: m.c.to_f32() as f64,
            d: m.d.to_f32() as f64,
            tx: m.tx.get() as f64 / 20.0,
            ty: m.ty.get() as f64 / 20.0,
        }
    }
    /// self ∘ other : applique other puis self.
    fn mul(&self, o: &M) -> M {
        M {
            a: self.a * o.a + self.c * o.b,
            b: self.b * o.a + self.d * o.b,
            c: self.a * o.c + self.c * o.d,
            d: self.b * o.c + self.d * o.d,
            tx: self.a * o.tx + self.c * o.ty + self.tx,
            ty: self.b * o.tx + self.d * o.ty + self.ty,
        }
    }
    fn apply(&self, x: f64, y: f64) -> (f64, f64) {
        (self.a * x + self.c * y + self.tx, self.b * x + self.d * y + self.ty)
    }
    fn svg(&self) -> String {
        format!("matrix({} {} {} {} {} {})", fm(self.a), fm(self.b), fm(self.c), fm(self.d), fm(self.tx), fm(self.ty))
    }
}

fn fm(v: f64) -> String {
    let s = format!("{:.3}", v);
    let s = s.trim_end_matches('0').trim_end_matches('.').to_string();
    if s == "-0" { "0".to_string() } else { s }
}

#[derive(Default, Clone, Copy)]
struct Bounds {
    x0: f64,
    y0: f64,
    x1: f64,
    y1: f64,
    set: bool,
}

impl Bounds {
    fn add(&mut self, x: f64, y: f64) {
        if !self.set {
            *self = Bounds { x0: x, y0: y, x1: x, y1: y, set: true };
        } else {
            self.x0 = self.x0.min(x);
            self.y0 = self.y0.min(y);
            self.x1 = self.x1.max(x);
            self.y1 = self.y1.max(y);
        }
    }
}

struct Bitmap {
    width: u32,
    height: u32,
    png_b64: String,
}

struct Exporter<'a> {
    chars: HashMap<u16, &'a Tag<'a>>,
    jpeg_tables: Option<&'a [u8]>,
    bitmaps: HashMap<u16, Option<Bitmap>>,
    defs: String,
    body: String,
    bounds: Bounds,
    def_counter: usize,
    warnings: Vec<String>,
}

fn color_css(c: &swf::Color) -> (String, f64) {
    (format!("#{:02x}{:02x}{:02x}", c.r, c.g, c.b), c.a as f64 / 255.0)
}

/// Segment d'une forme : chaque arête, avec ses styles.
#[derive(Clone)]
struct Edge {
    from: (Px, Px),
    to: (Px, Px),
    ctrl: Option<(Px, Px)>,
}

impl Edge {
    fn reversed(&self) -> Edge {
        Edge { from: self.to, to: self.from, ctrl: self.ctrl }
    }
}

fn chain_edges(mut edges: Vec<Edge>) -> Vec<Vec<Edge>> {
    // Relie les arêtes bout à bout pour former des chemins fermés.
    let mut paths = Vec::new();
    let eps = 0.01;
    while let Some(first) = edges.pop() {
        let mut path = vec![first];
        loop {
            let end = path.last().unwrap().to;
            let mut found = None;
            for (i, e) in edges.iter().enumerate() {
                if (e.from.0 - end.0).abs() < eps && (e.from.1 - end.1).abs() < eps {
                    found = Some(i);
                    break;
                }
            }
            match found {
                Some(i) => path.push(edges.remove(i)),
                None => break,
            }
            let start = path[0].from;
            let end = path.last().unwrap().to;
            if (start.0 - end.0).abs() < eps && (start.1 - end.1).abs() < eps {
                break;
            }
        }
        paths.push(path);
    }
    paths
}

fn path_d(paths: &[Vec<Edge>], close: bool) -> String {
    let mut d = String::new();
    for p in paths {
        if p.is_empty() {
            continue;
        }
        let _ = write!(d, "M{} {}", fm(p[0].from.0), fm(p[0].from.1));
        for e in p {
            match e.ctrl {
                Some(c) => {
                    let _ = write!(d, "Q{} {} {} {}", fm(c.0), fm(c.1), fm(e.to.0), fm(e.to.1));
                }
                None => {
                    let _ = write!(d, "L{} {}", fm(e.to.0), fm(e.to.1));
                }
            }
        }
        if close {
            d.push('Z');
        }
    }
    d
}

impl<'a> Exporter<'a> {
    fn new(tags: &'a [Tag<'a>]) -> Exporter<'a> {
        let mut chars = HashMap::new();
        let mut jpeg_tables = None;
        for t in tags {
            match t {
                Tag::DefineShape(s) => {
                    chars.insert(s.id, t);
                }
                Tag::DefineSprite(s) => {
                    chars.insert(s.id, t);
                }
                Tag::DefineButton2(b) | Tag::DefineButton(b) => {
                    chars.insert(b.id, t);
                }
                Tag::DefineBits { id, .. } | Tag::DefineBitsJpeg2 { id, .. } => {
                    chars.insert(*id, t);
                }
                Tag::DefineBitsJpeg3(j) => {
                    chars.insert(j.id, t);
                }
                Tag::DefineBitsLossless(l) => {
                    chars.insert(l.id, t);
                }
                Tag::DefineMorphShape(m) => {
                    chars.insert(m.id, t);
                }
                Tag::DefineText(x) | Tag::DefineText2(x) => {
                    chars.insert(x.id, t);
                }
                Tag::JpegTables(j) => {
                    jpeg_tables = Some(*j);
                }
                _ => {}
            }
        }
        Exporter { chars, jpeg_tables, bitmaps: HashMap::new(), defs: String::new(), body: String::new(), bounds: Bounds::default(), def_counter: 0, warnings: Vec::new() }
    }

    fn reset(&mut self) {
        self.defs.clear();
        self.body.clear();
        self.bounds = Bounds::default();
        self.def_counter = 0;
        self.warnings.clear();
    }

    fn next_id(&mut self, prefix: &str) -> String {
        self.def_counter += 1;
        format!("{}{}", prefix, self.def_counter)
    }

    fn bitmap(&mut self, id: u16) -> Option<(u32, u32, String)> {
        if !self.bitmaps.contains_key(&id) {
            let decoded = self.decode_bitmap(id);
            self.bitmaps.insert(id, decoded);
        }
        self.bitmaps.get(&id).and_then(|b| b.as_ref()).map(|b| (b.width, b.height, b.png_b64.clone()))
    }

    fn decode_bitmap(&mut self, id: u16) -> Option<Bitmap> {
        let tag = *self.chars.get(&id)?;
        let img: image::RgbaImage = match tag {
            Tag::DefineBits { jpeg_data, .. } => {
                let mut data = Vec::new();
                if let Some(t) = self.jpeg_tables {
                    data.extend_from_slice(t);
                }
                data.extend_from_slice(jpeg_data);
                decode_jpeg(&data)?
            }
            Tag::DefineBitsJpeg2 { jpeg_data, .. } => decode_jpeg(jpeg_data)?,
            Tag::DefineBitsJpeg3(j) => {
                let mut img = decode_jpeg(j.data)?;
                if !j.alpha_data.is_empty() {
                    let mut alpha = Vec::new();
                    let mut dec = flate2::read::ZlibDecoder::new(j.alpha_data);
                    if dec.read_to_end(&mut alpha).is_ok() && alpha.len() >= (img.width() * img.height()) as usize {
                        for (i, p) in img.pixels_mut().enumerate() {
                            p.0[3] = alpha[i];
                        }
                    }
                }
                img
            }
            Tag::DefineBitsLossless(l) => {
                let mut raw = Vec::new();
                let mut dec = flate2::read::ZlibDecoder::new(&l.data[..]);
                dec.read_to_end(&mut raw).ok()?;
                let (w, h) = (l.width as u32, l.height as u32);
                let mut img = image::RgbaImage::new(w, h);
                match l.format {
                    swf::BitmapFormat::Rgb32 => {
                        for y in 0..h {
                            for x in 0..w {
                                let i = ((y * w + x) * 4) as usize;
                                if i + 3 < raw.len() {
                                    let (a, r, g, b) = (raw[i], raw[i + 1], raw[i + 2], raw[i + 3]);
                                    let p = if l.version >= 2 && a > 0 {
                                        // couleurs prémultipliées
                                        [(r as u32 * 255 / a as u32).min(255) as u8, (g as u32 * 255 / a as u32).min(255) as u8, (b as u32 * 255 / a as u32).min(255) as u8, a]
                                    } else if l.version >= 2 {
                                        [0, 0, 0, 0]
                                    } else {
                                        [r, g, b, 255]
                                    };
                                    img.put_pixel(x, y, image::Rgba(p));
                                }
                            }
                        }
                    }
                    swf::BitmapFormat::Rgb15 => {
                        let stride = ((w * 2 + 3) / 4) * 4;
                        for y in 0..h {
                            for x in 0..w {
                                let i = (y * stride + x * 2) as usize;
                                if i + 1 < raw.len() {
                                    let v = ((raw[i] as u16) << 8) | raw[i + 1] as u16;
                                    let r = ((v >> 10) & 0x1f) as u8 * 8;
                                    let g = ((v >> 5) & 0x1f) as u8 * 8;
                                    let b = (v & 0x1f) as u8 * 8;
                                    img.put_pixel(x, y, image::Rgba([r, g, b, 255]));
                                }
                            }
                        }
                    }
                    swf::BitmapFormat::ColorMap8 { num_colors } => {
                        let n = num_colors as usize + 1;
                        let entry = if l.version >= 2 { 4 } else { 3 };
                        let palette = &raw[..(n * entry).min(raw.len())];
                        let stride = ((w + 3) / 4) * 4;
                        for y in 0..h {
                            for x in 0..w {
                                let i = n * entry + (y * stride + x) as usize;
                                if i < raw.len() {
                                    let idx = raw[i] as usize * entry;
                                    if idx + entry - 1 < palette.len() {
                                        let p = if entry == 4 { [palette[idx], palette[idx + 1], palette[idx + 2], palette[idx + 3]] } else { [palette[idx], palette[idx + 1], palette[idx + 2], 255] };
                                        img.put_pixel(x, y, image::Rgba(p));
                                    }
                                }
                            }
                        }
                    }
                }
                img
            }
            _ => return None,
        };
        let mut png = Vec::new();
        image::DynamicImage::ImageRgba8(img.clone()).write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png).ok()?;
        Some(Bitmap { width: img.width(), height: img.height(), png_b64: base64::engine::general_purpose::STANDARD.encode(&png) })
    }

    fn fill_attr(&mut self, style: &FillStyle, m: &M) -> (String, String) {
        match style {
            FillStyle::Color(c) => {
                let (hex, a) = color_css(c);
                (hex, if a < 1.0 { format!(" fill-opacity=\"{}\"", fm(a)) } else { String::new() })
            }
            FillStyle::LinearGradient(g) | FillStyle::RadialGradient(g) | FillStyle::FocalGradient { gradient: g, .. } => {
                let id = self.next_id("g");
                let gm = m.mul(&M::from_swf(&g.matrix));
                let linear = matches!(style, FillStyle::LinearGradient(_));
                let spread = match g.spread {
                    swf::GradientSpread::Pad => "pad",
                    swf::GradientSpread::Reflect => "reflect",
                    swf::GradientSpread::Repeat => "repeat",
                };
                let mut stops = String::new();
                for r in &g.records {
                    let (hex, a) = color_css(&r.color);
                    let _ = write!(stops, "<stop offset=\"{}\" stop-color=\"{}\" stop-opacity=\"{}\"/>", fm(r.ratio as f64 / 255.0), hex, fm(a));
                }
                if linear {
                    let _ = write!(self.defs, "<linearGradient id=\"{}\" gradientUnits=\"userSpaceOnUse\" x1=\"-819.2\" y1=\"0\" x2=\"819.2\" y2=\"0\" spreadMethod=\"{}\" gradientTransform=\"{}\">{}</linearGradient>", id, spread, gm.svg(), stops);
                } else {
                    let focal = match style {
                        FillStyle::FocalGradient { focal_point, .. } => focal_point.to_f32() as f64 * 819.2,
                        _ => 0.0,
                    };
                    let _ = write!(self.defs, "<radialGradient id=\"{}\" gradientUnits=\"userSpaceOnUse\" cx=\"0\" cy=\"0\" r=\"819.2\" fx=\"{}\" fy=\"0\" spreadMethod=\"{}\" gradientTransform=\"{}\">{}</radialGradient>", id, fm(focal), spread, gm.svg(), stops);
                }
                (format!("url(#{})", id), String::new())
            }
            FillStyle::Bitmap { id: bid, matrix, is_repeating, .. } => {
                match self.bitmap(*bid) {
                    Some((w, h, b64)) => {
                        let id = self.next_id("p");
                        let bm = m.mul(&M::from_swf(matrix));
                        // La matrice d'un remplissage bitmap est exprimée en twips : 20 unités par pixel.
                        let bm = M { a: bm.a * 20.0, b: bm.b * 20.0, c: bm.c * 20.0, d: bm.d * 20.0, tx: bm.tx, ty: bm.ty };
                        let _ = is_repeating; // un motif non répété est rarement débordé par sa forme
                        let (pw, ph) = (w, h);
                        let _ = write!(self.defs, "<pattern id=\"{}\" patternUnits=\"userSpaceOnUse\" width=\"{}\" height=\"{}\" patternTransform=\"{}\"><image width=\"{}\" height=\"{}\" href=\"data:image/png;base64,{}\"/></pattern>", id, pw, ph, bm.svg(), w, h, b64);
                        (format!("url(#{})", id), String::new())
                    }
                    None => {
                        self.warnings.push(format!("bitmap {} illisible", bid));
                        ("none".to_string(), String::new())
                    }
                }
            }
        }
    }

    fn render_shape(&mut self, shape: &swf::Shape, m: &M, extra: &str) {
        let mut fills = shape.styles.fill_styles.clone();
        let mut lines = shape.styles.line_styles.clone();
        // (arêtes, style de remplissage index base 1) ; style 0 = aucun
        let mut fill_edges: BTreeMap<(usize, usize), Vec<Edge>> = BTreeMap::new(); // (génération, style) -> arêtes
        let mut line_edges: BTreeMap<(usize, usize), Vec<Edge>> = BTreeMap::new();
        let mut generation = 0usize;
        let mut gen_styles: Vec<(Vec<FillStyle>, Vec<swf::LineStyle>)> = vec![(fills.clone(), lines.clone())];
        let (mut x, mut y) = (0.0f64, 0.0f64);
        let (mut f0, mut f1, mut ls) = (0usize, 0usize, 0usize);
        for rec in &shape.shape {
            match rec {
                ShapeRecord::StyleChange(sc) => {
                    if let Some(ns) = &sc.new_styles {
                        fills = ns.fill_styles.clone();
                        lines = ns.line_styles.clone();
                        generation += 1;
                        gen_styles.push((fills.clone(), lines.clone()));
                        f0 = 0;
                        f1 = 0;
                        ls = 0;
                    }
                    if let Some(p) = &sc.move_to {
                        x = p.x.get() as f64 / 20.0;
                        y = p.y.get() as f64 / 20.0;
                    }
                    if let Some(v) = sc.fill_style_0 {
                        f0 = v as usize;
                    }
                    if let Some(v) = sc.fill_style_1 {
                        f1 = v as usize;
                    }
                    if let Some(v) = sc.line_style {
                        ls = v as usize;
                    }
                }
                ShapeRecord::StraightEdge { delta } => {
                    let (nx, ny) = (x + delta.dx.get() as f64 / 20.0, y + delta.dy.get() as f64 / 20.0);
                    let e = Edge { from: (x, y), to: (nx, ny), ctrl: None };
                    if f0 > 0 {
                        fill_edges.entry((generation, f0)).or_default().push(e.reversed());
                    }
                    if f1 > 0 {
                        fill_edges.entry((generation, f1)).or_default().push(e.clone());
                    }
                    if ls > 0 {
                        line_edges.entry((generation, ls)).or_default().push(e);
                    }
                    x = nx;
                    y = ny;
                }
                ShapeRecord::CurvedEdge { control_delta, anchor_delta } => {
                    let (cx, cy) = (x + control_delta.dx.get() as f64 / 20.0, y + control_delta.dy.get() as f64 / 20.0);
                    let (nx, ny) = (cx + anchor_delta.dx.get() as f64 / 20.0, cy + anchor_delta.dy.get() as f64 / 20.0);
                    let e = Edge { from: (x, y), to: (nx, ny), ctrl: Some((cx, cy)) };
                    if f0 > 0 {
                        fill_edges.entry((generation, f0)).or_default().push(e.reversed());
                    }
                    if f1 > 0 {
                        fill_edges.entry((generation, f1)).or_default().push(e.clone());
                    }
                    if ls > 0 {
                        line_edges.entry((generation, ls)).or_default().push(e);
                    }
                    x = nx;
                    y = ny;
                }
            }
        }
        let b = &shape.shape_bounds;
        for (px, py) in [(b.x_min, b.y_min), (b.x_max, b.y_min), (b.x_min, b.y_max), (b.x_max, b.y_max)] {
            let (tx, ty) = m.apply(px.get() as f64 / 20.0, py.get() as f64 / 20.0);
            self.bounds.add(tx, ty);
        }
        let _ = write!(self.body, "<g transform=\"{}\"{}>", m.svg(), extra);
        for ((gen, idx), edges) in fill_edges {
            let style = gen_styles.get(gen).and_then(|g| g.0.get(idx - 1)).cloned();
            let Some(style) = style else { continue };
            // Le magenta pur est la couleur technique des zones remplacées à l'exécution : on l'omet.
            if let FillStyle::Color(c) = &style {
                if c.r == 255 && c.g == 0 && c.b == 255 {
                    continue;
                }
            }
            let (fill, attrs) = self.fill_attr(&style, m);
            let paths = chain_edges(edges);
            let _ = write!(self.body, "<path fill=\"{}\"{} fill-rule=\"{}\" d=\"{}\"/>", fill, attrs, if shape.flags.contains(swf::ShapeFlag::NON_ZERO_WINDING_RULE) { "nonzero" } else { "evenodd" }, path_d(&paths, true));
        }
        for ((gen, idx), edges) in line_edges {
            let style = gen_styles.get(gen).and_then(|g| g.1.get(idx - 1)).cloned();
            let Some(style) = style else { continue };
            let width = (style.width().get() as f64 / 20.0).max(0.5);
            let (stroke, attrs) = match style.fill_style() {
                FillStyle::Color(c) => {
                    let (hex, a) = color_css(c);
                    (hex, if a < 1.0 { format!(" stroke-opacity=\"{}\"", fm(a)) } else { String::new() })
                }
                other => {
                    let (f, _) = self.fill_attr(other, m);
                    (f, String::new())
                }
            };
            let paths = chain_edges(edges);
            let _ = write!(self.body, "<path fill=\"none\" stroke=\"{}\" stroke-width=\"{}\"{} stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"{}\"/>", stroke, fm(width), attrs, path_d(&paths, false));
        }
        self.body.push_str("</g>");
    }

    fn color_transform_attr(&mut self, ct: Option<&swf::ColorTransform>) -> String {
        let Some(ct) = ct else { return String::new() };
        let mult = [ct.r_multiply.to_f32() as f64, ct.g_multiply.to_f32() as f64, ct.b_multiply.to_f32() as f64, ct.a_multiply.to_f32() as f64];
        let add = [ct.r_add as f64 / 255.0, ct.g_add as f64 / 255.0, ct.b_add as f64 / 255.0, ct.a_add as f64 / 255.0];
        if mult == [1.0, 1.0, 1.0, 1.0] && add == [0.0, 0.0, 0.0, 0.0] {
            return String::new();
        }
        if mult[0] == 1.0 && mult[1] == 1.0 && mult[2] == 1.0 && add == [0.0, 0.0, 0.0, 0.0] {
            return format!(" opacity=\"{}\"", fm(mult[3]));
        }
        let id = self.next_id("ct");
        let _ = write!(self.defs, "<filter id=\"{}\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"{} 0 0 0 {} 0 {} 0 0 {} 0 0 {} 0 {} 0 0 0 {} {}\"/></filter>", id, fm(mult[0]), fm(add[0]), fm(mult[1]), fm(add[1]), fm(mult[2]), fm(add[2]), fm(mult[3]), fm(add[3]));
        format!(" filter=\"url(#{})\"", id)
    }

    fn render_char(&mut self, id: u16, m: &M, ct: Option<&swf::ColorTransform>, depth: usize) {
        if depth > 12 {
            return;
        }
        let Some(tag) = self.chars.get(&id).copied() else {
            self.warnings.push(format!("caractère {} inconnu", id));
            return;
        };
        let extra = self.color_transform_attr(ct);
        match tag {
            Tag::DefineShape(s) => self.render_shape(s, m, &extra),
            Tag::DefineSprite(sp) => {
                let _ = write!(self.body, "<g{}>", extra);
                self.render_timeline(&sp.tags, m, depth + 1, 0);
                self.body.push_str("</g>");
            }
            Tag::DefineButton2(b) | Tag::DefineButton(b) => {
                let _ = write!(self.body, "<g{}>", extra);
                let mut recs: Vec<&swf::ButtonRecord> = b.records.iter().filter(|r| r.states.contains(swf::ButtonState::UP)).collect();
                recs.sort_by_key(|r| r.depth);
                for r in recs {
                    let cm = m.mul(&M::from_swf(&r.matrix));
                    self.render_char(r.id, &cm, Some(&r.color_transform), depth + 1);
                }
                self.body.push_str("</g>");
            }
            Tag::DefineBits { .. } | Tag::DefineBitsJpeg2 { .. } | Tag::DefineBitsJpeg3(_) | Tag::DefineBitsLossless(_) => {
                if let Some((w, h, b64)) = self.bitmap(id) {
                    let _ = write!(self.body, "<image transform=\"{}\" width=\"{}\" height=\"{}\"{} href=\"data:image/png;base64,{}\"/>", m.svg(), w, h, extra, b64);
                    for (px, py) in [(0.0, 0.0), (w as f64, 0.0), (0.0, h as f64), (w as f64, h as f64)] {
                        let (tx, ty) = m.apply(px, py);
                        self.bounds.add(tx, ty);
                    }
                }
            }
            Tag::DefineText(t) | Tag::DefineText2(t) => {
                // Le texte statique n'est pas rendu (glyphes de police) ; on garde ses limites.
                let b = &t.bounds;
                for (px, py) in [(b.x_min, b.y_min), (b.x_max, b.y_max)] {
                    let (tx, ty) = m.apply(px.get() as f64 / 20.0, py.get() as f64 / 20.0);
                    self.bounds.add(tx, ty);
                }
                self.warnings.push(format!("texte statique {} non rendu", id));
            }
            Tag::DefineMorphShape(_) => self.warnings.push(format!("forme morphée {} non rendue", id)),
            _ => {}
        }
    }

    /// Rend la première image (frame) d'une timeline, ou la frame `frame` si indiquée.
    fn render_timeline(&mut self, tags: &[Tag], m: &M, depth: usize, frame: usize) {
        let mut display: BTreeMap<u16, (u16, M, Option<swf::ColorTransform>)> = BTreeMap::new();
        let mut current_frame = 0usize;
        for t in tags {
            match t {
                Tag::PlaceObject(p) => {
                    let pm = p.matrix.as_ref().map(M::from_swf);
                    match p.action {
                        swf::PlaceObjectAction::Place(cid) | swf::PlaceObjectAction::Replace(cid) => {
                            let prev = display.get(&p.depth).cloned();
                            let mm = pm.or(prev.as_ref().map(|x| x.1)).unwrap_or(M::identity());
                            let ct = p.color_transform.clone().or(prev.and_then(|x| x.2));
                            display.insert(p.depth, (cid, mm, ct));
                        }
                        swf::PlaceObjectAction::Modify => {
                            if let Some(entry) = display.get_mut(&p.depth) {
                                if let Some(mm) = pm {
                                    entry.1 = mm;
                                }
                                if let Some(ct) = &p.color_transform {
                                    entry.2 = Some(ct.clone());
                                }
                            }
                        }
                    }
                }
                Tag::RemoveObject(r) => {
                    display.remove(&r.depth);
                }
                Tag::ShowFrame => {
                    if current_frame >= frame {
                        break;
                    }
                    current_frame += 1;
                }
                _ => {}
            }
        }
        let items: Vec<(u16, M, Option<swf::ColorTransform>)> = display.values().cloned().collect();
        for (cid, cm, ct) in items {
            let full = m.mul(&cm);
            self.render_char(cid, &full, ct.as_ref(), depth);
        }
    }

    fn svg(&self) -> String {
        let b = self.bounds;
        let (x0, y0, x1, y1) = if b.set { (b.x0.floor(), b.y0.floor(), b.x1.ceil(), b.y1.ceil()) } else { (0.0, 0.0, 1.0, 1.0) };
        let (w, h) = ((x1 - x0).max(1.0), (y1 - y0).max(1.0));
        format!("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{}\" height=\"{}\" viewBox=\"{} {} {} {}\"><defs>{}</defs>{}</svg>", fm(w), fm(h), fm(x0), fm(y0), fm(w), fm(h), self.defs, self.body)
    }
}

fn decode_jpeg(data: &[u8]) -> Option<image::RgbaImage> {
    // Certains JPEG Flash commencent par un marqueur EOI/SOI parasite.
    let mut d = data;
    if d.len() > 4 && d[0] == 0xff && d[1] == 0xd9 && d[2] == 0xff && d[3] == 0xd8 {
        d = &d[4..];
    }
    image::load_from_memory_with_format(d, image::ImageFormat::Jpeg).ok().map(|i| i.to_rgba8())
}

fn safe_name(n: &str) -> String {
    n.chars().map(|c| if c.is_ascii_alphanumeric() || c == '_' || c == '-' { c } else { '_' }).collect()
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 3 {
        eprintln!("usage : swfsvg <fichier.swf> <dossier> [nomExport ...]");
        std::process::exit(2);
    }
    let file = File::open(&args[1]).expect("ouverture");
    let buf = swf::decompress_swf(file).expect("décompression");
    let movie = swf::parse_swf(&buf).expect("analyse");
    std::fs::create_dir_all(&args[2]).expect("dossier");
    let wanted: Vec<String> = args[3..].to_vec();
    let mut exports: Vec<(u16, String)> = Vec::new();
    for t in &movie.tags {
        if let Tag::ExportAssets(list) = t {
            for a in list {
                exports.push((a.id, a.name.to_str_lossy(ENC).to_string()));
            }
        }
    }
    let mut exporter = Exporter::new(&movie.tags);
    let mut index = String::new();
    for (id, name) in &exports {
        if !wanted.is_empty() && !wanted.iter().any(|w| w == name) {
            continue;
        }
        exporter.reset();
        exporter.render_char(*id, &M::identity(), None, 0);
        let svg = exporter.svg();
        let b = exporter.bounds;
        let path = format!("{}/{}.svg", args[2], safe_name(name));
        let mut f = File::create(&path).expect("écriture");
        f.write_all(svg.as_bytes()).expect("écriture");
        let _ = writeln!(index, "{}\t{}\t{}\t{}\t{}\t{}\t{}", name, id, fm(b.x0), fm(b.y0), fm(b.x1 - b.x0), fm(b.y1 - b.y0), exporter.warnings.join("; "));
    }
    let mut f = File::create(format!("{}/index.tsv", args[2])).expect("index");
    f.write_all(index.as_bytes()).expect("index");
}
