//! Exporte des symboles (formes, clips, boutons) d'un SWF en SVG.
//!
//! usage :
//!   swfsvg [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier> [nomExport ...]
//!   swfsvg --scene [--name NOM] [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier>
//!   swfsvg --list <fichier.swf>
//!   (avec --instance NOM ou --sans-instance NOM pour les deux premières formes)
//!
//! Sans nom d'export, tous les symboles d'`ExportAssets` sont rendus. `--scene` rend la timeline
//! principale (icônes d'objets, émotes, portraits : formes posées sur la scène, sans export).
//! `--frame` choisit l'image de la timeline demandée, les clips imbriqués ayant joué depuis leur
//! création comme à l'écran (cycles de marche) ; `--list` décrit les symboles sans rien rendre,
//! avec la fin de chaque animation (colonne `fin` : boucle, arrêt, retour au repos ou suite).
//! `--instance NOM` ne rend, de la timeline demandée, que l'enfant nommé NOM (nom d'instance du
//! `PlaceObject`, sans tenir compte de la casse), dans le temps de cette timeline et à sa place ;
//! `--sans-instance NOM` rend tout sauf lui. Les masques suivent leur contenu.
use base64::Engine;
use std::collections::{BTreeMap, HashMap};
use std::fmt::Write as _;
use std::fs::File;
use std::io::{Read, Write};
use swf::{FillStyle, Matrix, ShapeRecord, Tag};

const USAGE: &str = "usage : swfsvg [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier> [nomExport ...]
        swfsvg --scene [--name NOM] [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier>
        swfsvg --list <fichier.swf>
        (--instance NOM : seul l'enfant nommé de la timeline demandée ; --sans-instance NOM : tout sauf lui)";

/// Profondeur maximale d'imbrication suivie (clips dans des clips).
const MAX_DEPTH: usize = 12;

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
    /// Partie linéaire à six décimales : les dégradés ont des coefficients de l'ordre de 0,001
    /// (repère de 1 638,4 pixels) qu'un arrondi à trois décimales rend singuliers, et cairo refuse
    /// alors tout le dessin.
    fn svg(&self) -> String {
        format!("matrix({} {} {} {} {} {})", fm_fine(self.a), fm_fine(self.b), fm_fine(self.c), fm_fine(self.d), fm(self.tx), fm(self.ty))
    }
}

fn fm(v: f64) -> String {
    trim_number(format!("{:.3}", v))
}

fn fm_fine(v: f64) -> String {
    trim_number(format!("{:.6}", v))
}

fn trim_number(s: String) -> String {
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

impl Bounds {
    fn union(&self, o: &Bounds) -> Bounds {
        match (self.set, o.set) {
            (false, _) => *o,
            (_, false) => *self,
            _ => Bounds { x0: self.x0.min(o.x0), y0: self.y0.min(o.y0), x1: self.x1.max(o.x1), y1: self.y1.max(o.y1), set: true },
        }
    }
    fn intersection(&self, o: &Bounds) -> Bounds {
        if !self.set || !o.set {
            return Bounds::default();
        }
        let b = Bounds { x0: self.x0.max(o.x0), y0: self.y0.max(o.y0), x1: self.x1.min(o.x1), y1: self.y1.min(o.y1), set: true };
        if b.x0 < b.x1 && b.y0 < b.y1 { b } else { Bounds::default() }
    }
}

struct Bitmap {
    width: u32,
    height: u32,
    png_b64: String,
}

/// Ce qui règle la lecture d'une timeline : nombre d'images et premier `stop()` rencontré.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct Timeline {
    frames: usize,
    stop: Option<usize>,
}

/// Fin d'une animation jouée par le client (colonne `fin` de `--list`), d'après le script de
/// l'image où elle s'arrête : `boucle` (aucun script d'arrêt), `arret` (`stop()` : la dernière image
/// reste affichée), `static` (`GAC.applyAnim(this, "static")` : retour à la pose de repos, ou
/// `removeMovieClip` : le clip disparaît), `suite:<anim>` (`GAC.applyAnim(this, "<anim>")`).
#[derive(Clone, Debug, PartialEq)]
enum End {
    Loop,
    Stop,
    Static,
    Next(String),
}

impl End {
    fn label(&self) -> String {
        match self {
            End::Loop => "boucle".into(),
            End::Stop => "arret".into(),
            End::Static => "static".into(),
            End::Next(name) => format!("suite:{}", name),
        }
    }

    /// Vrai pour un script qui remplace ou retire le clip (il l'emporte sur un `stop()`).
    fn replaces(&self) -> bool {
        matches!(self, End::Static | End::Next(_))
    }
}

/// Objet de la liste d'affichage d'une timeline, à une profondeur donnée.
#[derive(Clone)]
struct Placed {
    id: u16,
    matrix: M,
    ct: Option<swf::ColorTransform>,
    ratio: u16,
    clip_depth: Option<u16>,
    /// Image (base 0) de la timeline où cette instance a été créée.
    born: usize,
    /// Nom d'instance (`PlaceObject` nommé), pour `--instance` et `--sans-instance`.
    name: Option<String>,
}

/// Filtre des enfants de la timeline demandée (`--instance`, `--sans-instance`).
#[derive(Clone, Debug, PartialEq)]
enum Instance {
    /// Seul l'enfant de ce nom est rendu.
    Only(String),
    /// Tout est rendu sauf l'enfant de ce nom.
    Without(String),
}

impl Instance {
    fn name(&self) -> &str {
        match self {
            Instance::Only(n) | Instance::Without(n) => n,
        }
    }
}

struct Exporter<'a> {
    chars: HashMap<u16, &'a Tag<'a>>,
    jpeg_tables: Option<&'a [u8]>,
    bitmaps: HashMap<u16, Option<Bitmap>>,
    timelines: HashMap<u16, Timeline>,
    cycles: HashMap<u16, usize>,
    /// Fin de chaque caractère joué comme clip (`--list`), mise en cache.
    ends: HashMap<u16, End>,
    defs: String,
    body: String,
    bounds: Bounds,
    def_counter: usize,
    warnings: Vec<String>,
    /// Vrai pendant le rendu de la géométrie d'un masque (`<clipPath>`) : chemins seuls, sans style.
    clip_mode: bool,
    /// Nombre de transformations de couleur englobantes qui remplacent la teinte (multiplicateurs
    /// RGB nuls, comme `Color.setRGB`) : sous elles, les aplats magenta prennent cette teinte.
    recolored: usize,
    /// Filtre de la timeline demandée (`--instance`, `--sans-instance`).
    instance: Option<Instance>,
    /// Vrai dès qu'une image rendue de la cible a contenu l'instance filtrée.
    instance_found: bool,
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
        Exporter {
            chars,
            jpeg_tables,
            bitmaps: HashMap::new(),
            timelines: HashMap::new(),
            cycles: HashMap::new(),
            ends: HashMap::new(),
            defs: String::new(),
            body: String::new(),
            bounds: Bounds::default(),
            def_counter: 0,
            warnings: Vec::new(),
            clip_mode: false,
            recolored: 0,
            instance: None,
            instance_found: false,
        }
    }

    fn reset(&mut self) {
        self.defs.clear();
        self.body.clear();
        self.bounds = Bounds::default();
        self.def_counter = 0;
        self.warnings.clear();
        self.clip_mode = false;
        self.recolored = 0;
    }

    fn warn(&mut self, message: String) {
        if !self.warnings.contains(&message) {
            self.warnings.push(message);
        }
    }

    /// Timeline d'un clip (`DefineSprite`), mise en cache.
    fn sprite_timeline(&mut self, sprite: &swf::Sprite) -> Timeline {
        *self.timelines.entry(sprite.id).or_insert_with(|| timeline_info(&sprite.tags))
    }

    /// Type affiché par `--list` pour un caractère.
    fn kind(&self, id: u16) -> &'static str {
        match self.chars.get(&id) {
            Some(Tag::DefineShape(_)) => "forme",
            Some(Tag::DefineSprite(_)) => "clip",
            Some(Tag::DefineButton(_)) | Some(Tag::DefineButton2(_)) => "bouton",
            Some(Tag::DefineMorphShape(_)) => "morph",
            Some(Tag::DefineText(_)) | Some(Tag::DefineText2(_)) => "texte",
            Some(Tag::DefineBits { .. }) | Some(Tag::DefineBitsJpeg2 { .. }) | Some(Tag::DefineBitsJpeg3(_)) | Some(Tag::DefineBitsLossless(_)) => "bitmap",
            Some(_) => "autre",
            None => "absent",
        }
    }

    /// Nombre d'images utiles d'un caractère joué comme clip imbriqué : sa timeline jusqu'à son
    /// `stop()` et celles des clips qu'il contient (un cycle de marche est souvent un clip
    /// d'une image qui contient le vrai cycle).
    fn cycle(&mut self, id: u16, depth: usize) -> usize {
        if depth > MAX_DEPTH {
            return 1;
        }
        if let Some(n) = self.cycles.get(&id) {
            return *n;
        }
        // Valeur provisoire : un clip qui se contient lui-même (fichier corrompu) ne boucle pas.
        self.cycles.insert(id, 1);
        let n = match self.chars.get(&id).copied() {
            Some(Tag::DefineSprite(sp)) => {
                let info = self.sprite_timeline(sp);
                self.cycle_of_timeline(&sp.tags, info, false, depth + 1)
            }
            Some(Tag::DefineButton(b)) | Some(Tag::DefineButton2(b)) => {
                let ids: Vec<u16> = b.records.iter().filter(|r| r.states.contains(swf::ButtonState::UP)).map(|r| r.id).collect();
                ids.into_iter().map(|c| self.cycle(c, depth + 1)).max().unwrap_or(1)
            }
            _ => 1,
        };
        self.cycles.insert(id, n);
        n
    }

    /// Nombre d'images utiles d'une timeline. `root` : timeline demandée (toutes ses images
    /// comptent, comme pour `gotoAndStop`) ; sinon elle s'arrête sur son premier `stop()`.
    fn cycle_of_timeline(&mut self, tags: &[Tag], info: Timeline, root: bool, depth: usize) -> usize {
        let own = if root { info.frames } else { info.stop.map(|s| s + 1).unwrap_or(info.frames) };
        // Une timeline qui boucle recrée à chaque tour les clips posés après sa première image.
        let looping = info.frames > 1 && (root || info.stop.is_none());
        let mut best = own.max(1);
        let mut frame = 0usize;
        let mut children: Vec<(usize, u16)> = Vec::new();
        for t in tags {
            match t {
                Tag::PlaceObject(p) => {
                    if let swf::PlaceObjectAction::Place(cid) | swf::PlaceObjectAction::Replace(cid) = p.action {
                        if frame < own && (!looping || frame == 0) {
                            children.push((frame, cid));
                        }
                    }
                }
                Tag::ShowFrame => frame += 1,
                _ => {}
            }
        }
        for (born, cid) in children {
            best = best.max(born + self.cycle(cid, depth + 1));
        }
        best
    }

    /// Fin d'un caractère joué comme clip imbriqué (voir [`End`]).
    fn end_of(&mut self, id: u16, depth: usize) -> End {
        if depth > MAX_DEPTH {
            return End::Stop;
        }
        if let Some(e) = self.ends.get(&id) {
            return e.clone();
        }
        // Valeur provisoire : un clip qui se contient lui-même (fichier corrompu) ne boucle pas.
        self.ends.insert(id, End::Stop);
        let e = match self.chars.get(&id).copied() {
            Some(Tag::DefineSprite(sp)) => {
                let info = self.sprite_timeline(sp);
                self.end_of_timeline(&sp.tags, info, false, depth + 1)
            }
            _ => End::Stop,
        };
        self.ends.insert(id, e.clone());
        e
    }

    /// Fin d'une timeline jouée par le client depuis sa première image. La timeline qui fixe la
    /// durée (`images`) décide : la sienne si elle est au moins aussi longue que ses clips (premier
    /// script d'arrêt rencontré, sinon boucle), sinon celle du clip imbriqué qui finit le plus tard.
    /// Une timeline d'une image sans clip plus long ne bouge pas : `arret`. Les conditions ne sont
    /// pas évaluées.
    fn end_of_timeline(&mut self, tags: &[Tag], info: Timeline, root: bool, depth: usize) -> End {
        let own = if root { info.frames } else { info.stop.map(|s| s + 1).unwrap_or(info.frames) }.max(1);
        let looping = info.frames > 1 && (root || info.stop.is_none());
        let mut frame = 0usize;
        let mut own_end: Option<(usize, End)> = None;
        let mut children: Vec<(usize, u16)> = Vec::new();
        for t in tags {
            match t {
                Tag::DoAction(code) => {
                    if let Some(e) = script_end(code) {
                        // Dans une même image, un script qui remplace le clip l'emporte sur stop().
                        let better = match &own_end {
                            None => true,
                            Some((f, old)) => *f == frame && !old.replaces() && e.replaces(),
                        };
                        if better {
                            own_end = Some((frame, e));
                        }
                    }
                }
                Tag::PlaceObject(p) => {
                    if let swf::PlaceObjectAction::Place(cid) | swf::PlaceObjectAction::Replace(cid) = p.action {
                        if frame < own && (!looping || frame == 0) {
                            children.push((frame, cid));
                        }
                    }
                }
                Tag::ShowFrame => frame += 1,
                _ => {}
            }
        }
        let mut longest = 0usize;
        let mut child_end: Option<End> = None;
        for (born, cid) in children {
            let finish = born + self.cycle(cid, depth + 1);
            let e = self.end_of(cid, depth + 1);
            let loops = child_end.as_ref().is_some_and(|c| *c == End::Loop);
            if finish > longest || (finish == longest && loops && e != End::Loop) {
                longest = finish;
                child_end = Some(e);
            }
        }
        let own_end = own_end.filter(|(f, _)| *f < info.frames).map(|(_, e)| e);
        if longest > own {
            return child_end.unwrap_or(End::Loop);
        }
        if let Some(e) = own_end {
            return e;
        }
        if longest == own {
            if let Some(e) = child_end.filter(|e| *e != End::Loop) {
                return e;
            }
        }
        if info.frames > 1 {
            End::Loop
        } else {
            End::Stop
        }
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

    /// Remplissage d'un tracé posé dans `<g transform="m">` : l'espace utilisateur du tracé est déjà
    /// celui de la forme, donc les matrices de dégradé et de bitmap s'expriment seules, sans `m`
    /// (l'y ajouter appliquait deux fois la pose du clip : motifs minuscules et répétés).
    fn fill_attr(&mut self, style: &FillStyle) -> (String, String) {
        match style {
            FillStyle::Color(c) => {
                let (hex, a) = color_css(c);
                (hex, if a < 1.0 { format!(" fill-opacity=\"{}\"", fm(a)) } else { String::new() })
            }
            FillStyle::LinearGradient(g) | FillStyle::RadialGradient(g) | FillStyle::FocalGradient { gradient: g, .. } => {
                let gm = M::from_swf(&g.matrix);
                // Dégradé dégénéré (matrice presque singulière : une lame de quelques millièmes de
                // pixel, fréquente dans les petits éclats des icônes) : cairo refuse de le peindre et
                // abandonne toute l'image. Le carré du dégradé (1 638,4 px de côté) couvre alors
                // moins de 0,05 px² : une seule couleur, celle du milieu, suffit.
                if (gm.a * gm.d - gm.b * gm.c).abs() * 1638.4 * 1638.4 < 0.05 {
                    if let Some(r) = g.records.get(g.records.len() / 2) {
                        let (hex, a) = color_css(&r.color);
                        return (hex, if a < 1.0 { format!(" fill-opacity=\"{}\"", fm(a)) } else { String::new() });
                    }
                }
                let id = self.next_id("g");
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
                        // La matrice d'un remplissage bitmap envoie les pixels de l'image vers l'espace de la forme
                        // en twips (20 par pixel) : en pixels, sa partie linéaire est donc divisée par 20.
                        let fm = M::from_swf(matrix);
                        let bm = M { a: fm.a / 20.0, b: fm.b / 20.0, c: fm.c / 20.0, d: fm.d / 20.0, tx: fm.tx, ty: fm.ty };
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
        if fill_edges.is_empty() && line_edges.is_empty() {
            // Forme vide (souvent une forme morphée de remplacement) : rien à dessiner ni à cadrer.
            return;
        }
        let b = &shape.shape_bounds;
        for (px, py) in [(b.x_min, b.y_min), (b.x_max, b.y_min), (b.x_min, b.y_max), (b.x_max, b.y_max)] {
            let (tx, ty) = m.apply(px.get() as f64 / 20.0, py.get() as f64 / 20.0);
            self.bounds.add(tx, ty);
        }
        if self.clip_mode {
            // Géométrie d'un masque : seules les surfaces remplies comptent, quelle que soit leur couleur.
            for (_, edges) in fill_edges {
                let paths = chain_edges(edges);
                let _ = write!(self.body, "<path transform=\"{}\" d=\"{}\"/>", m.svg(), path_d(&paths, true));
            }
            return;
        }
        let _ = write!(self.body, "<g transform=\"{}\"{}>", m.svg(), extra);
        for ((gen, idx), edges) in fill_edges {
            let style = gen_styles.get(gen).and_then(|g| g.0.get(idx - 1)).cloned();
            let Some(style) = style else { continue };
            // Le magenta pur est la couleur technique des zones remplacées à l'exécution : on l'omet,
            // sauf si une transformation de couleur du SWF lui donne déjà sa teinte.
            if let FillStyle::Color(c) = &style {
                if c.r == 255 && c.g == 0 && c.b == 255 && self.recolored == 0 {
                    continue;
                }
            }
            let (fill, attrs) = self.fill_attr(&style);
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
                    let (f, _) = self.fill_attr(other);
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

    /// Rend un caractère. `age` : nombre d'images écoulées depuis la création de l'instance
    /// (sert aux clips imbriqués) ; `ratio` : position d'une forme morphée (0 à 65535).
    fn render_char(&mut self, id: u16, m: &M, ct: Option<&swf::ColorTransform>, depth: usize, age: usize, ratio: u16) {
        if depth > MAX_DEPTH {
            self.warn(format!("imbrication de plus de {} niveaux ignorée", MAX_DEPTH));
            return;
        }
        let Some(tag) = self.chars.get(&id).copied() else {
            self.warn(format!("caractère {} inconnu", id));
            return;
        };
        let extra = if self.clip_mode { String::new() } else { self.color_transform_attr(ct) };
        let replaces = ct.is_some_and(|c| c.r_multiply.to_f32() == 0.0 && c.g_multiply.to_f32() == 0.0 && c.b_multiply.to_f32() == 0.0);
        if replaces {
            self.recolored += 1;
        }
        match tag {
            Tag::DefineShape(s) => self.render_shape(s, m, &extra),
            Tag::DefineSprite(sp) => {
                let info = self.sprite_timeline(sp);
                self.group(&extra, |e| e.render_timeline(&sp.tags, info, m, depth + 1, age, false));
            }
            Tag::DefineButton2(b) | Tag::DefineButton(b) => {
                let mut recs: Vec<&swf::ButtonRecord> = b.records.iter().filter(|r| r.states.contains(swf::ButtonState::UP)).collect();
                recs.sort_by_key(|r| r.depth);
                self.group(&extra, |e| {
                    for r in recs {
                        let cm = m.mul(&M::from_swf(&r.matrix));
                        e.render_char(r.id, &cm, Some(&r.color_transform), depth + 1, age, 0);
                    }
                });
            }
            Tag::DefineBits { .. } | Tag::DefineBitsJpeg2 { .. } | Tag::DefineBitsJpeg3(_) | Tag::DefineBitsLossless(_) => {
                if let Some((w, h, b64)) = self.bitmap(id) {
                    if self.clip_mode {
                        let _ = write!(self.body, "<path transform=\"{}\" d=\"M0 0H{}V{}H0Z\"/>", m.svg(), w, h);
                    } else {
                        let _ = write!(self.body, "<image transform=\"{}\" width=\"{}\" height=\"{}\"{} href=\"data:image/png;base64,{}\"/>", m.svg(), w, h, extra, b64);
                    }
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
                self.warn(format!("texte statique {} non rendu", id));
            }
            Tag::DefineMorphShape(morph) => {
                let shape = morph_frame(morph, ratio);
                self.render_shape(&shape, m, &extra);
            }
            _ => {}
        }
        if replaces {
            self.recolored -= 1;
        }
    }

    /// Entoure le rendu de `f` d'un groupe portant `extra` (transformation de couleur) ; dans un
    /// masque, les groupes sont inutiles et omis.
    fn group<F: FnOnce(&mut Self)>(&mut self, extra: &str, f: F) {
        if self.clip_mode {
            f(self);
        } else {
            let _ = write!(self.body, "<g{}>", extra);
            f(self);
            self.body.push_str("</g>");
        }
    }

    /// Rend une timeline `age` images après sa création. Une timeline `root` (symbole demandé ou
    /// scène) affiche directement son image `age` (modulo sa longueur), comme après un
    /// `gotoAndStop` ; une timeline imbriquée joue comme à l'écran : elle boucle et s'arrête sur
    /// son premier `stop()`. Les clips qu'elle contient vieillissent depuis leur création.
    fn render_timeline(&mut self, tags: &[Tag], info: Timeline, m: &M, depth: usize, age: usize, root: bool) {
        let (frame, persistent) = play_position(info, age, root);
        let mut display: BTreeMap<u16, Placed> = BTreeMap::new();
        let mut current_frame = 0usize;
        for t in tags {
            match t {
                Tag::PlaceObject(p) => {
                    let pm = p.matrix.as_ref().map(M::from_swf);
                    match p.action {
                        swf::PlaceObjectAction::Place(cid) | swf::PlaceObjectAction::Replace(cid) => {
                            let prev = display.get(&p.depth).cloned();
                            // Remplacer un caractère par lui-même garde l'instance (et son âge).
                            let kept = matches!(p.action, swf::PlaceObjectAction::Replace(_)) && prev.as_ref().is_some_and(|x| x.id == cid);
                            let named = p.name.map(|n| n.to_str_lossy(ENC).to_string());
                            let placed = Placed {
                                id: cid,
                                matrix: pm.or(prev.as_ref().map(|x| x.matrix)).unwrap_or(M::identity()),
                                ct: p.color_transform.or(prev.as_ref().and_then(|x| x.ct)),
                                ratio: p.ratio.or(prev.as_ref().map(|x| x.ratio)).unwrap_or(0),
                                clip_depth: p.clip_depth.or(prev.as_ref().and_then(|x| x.clip_depth)),
                                born: if kept { prev.as_ref().map_or(current_frame, |x| x.born) } else { current_frame },
                                name: named.or(if kept { prev.and_then(|x| x.name) } else { None }),
                            };
                            display.insert(p.depth, placed);
                        }
                        swf::PlaceObjectAction::Modify => {
                            if let Some(entry) = display.get_mut(&p.depth) {
                                if let Some(mm) = pm {
                                    entry.matrix = mm;
                                }
                                if let Some(ct) = p.color_transform {
                                    entry.ct = Some(ct);
                                }
                                if let Some(r) = p.ratio {
                                    entry.ratio = r;
                                }
                                if let Some(c) = p.clip_depth {
                                    entry.clip_depth = Some(c);
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
        let mut items: Vec<(u16, Placed, usize)> = display
            .into_iter()
            .map(|(d, p)| {
                let child_age = child_age(age, frame, persistent, p.born);
                (d, p, child_age)
            })
            .collect();
        if root {
            if let Some(filter) = self.instance.clone() {
                let (kept, found) = filter_instances(items, &filter);
                self.instance_found |= found;
                items = kept;
            }
        }
        self.render_items(&items, m, depth);
    }

    /// Rend une liste d'affichage triée par profondeur ; un objet `clip_depth` masque les objets
    /// suivants jusqu'à cette profondeur incluse.
    fn render_items(&mut self, items: &[(u16, Placed, usize)], m: &M, depth: usize) {
        let mut i = 0;
        while i < items.len() {
            let (_, p, age) = &items[i];
            let full = m.mul(&p.matrix);
            match p.clip_depth {
                Some(limit) => {
                    let mut j = i + 1;
                    while j < items.len() && items[j].0 <= limit {
                        j += 1;
                    }
                    if self.clip_mode {
                        // Masque dans la géométrie d'un masque : seul le contenu visible compte.
                        self.render_items(&items[i + 1..j], m, depth);
                    } else {
                        self.render_masked(p, &full, *age, &items[i + 1..j], m, depth);
                    }
                    i = j;
                }
                None => {
                    self.render_char(p.id, &full, p.ct.as_ref(), depth, *age, p.ratio);
                    i += 1;
                }
            }
        }
    }

    /// Rend `items` découpés par la forme `mask` (`<clipPath>`) ; le cadre retenu est
    /// l'intersection du cadre du masque et de celui du contenu.
    fn render_masked(&mut self, mask: &Placed, mask_m: &M, mask_age: usize, items: &[(u16, Placed, usize)], m: &M, depth: usize) {
        let outer_body = std::mem::take(&mut self.body);
        let outer_bounds = std::mem::take(&mut self.bounds);
        self.clip_mode = true;
        self.render_char(mask.id, mask_m, None, depth, mask_age, mask.ratio);
        self.clip_mode = false;
        let clip_body = std::mem::take(&mut self.body);
        let mask_bounds = std::mem::take(&mut self.bounds);
        self.render_items(items, m, depth);
        let content = std::mem::replace(&mut self.body, outer_body);
        let content_bounds = std::mem::replace(&mut self.bounds, outer_bounds);
        let visible = mask_bounds.intersection(&content_bounds);
        if content.is_empty() || clip_body.is_empty() || !visible.set {
            return; // un masque vide cache tout son contenu
        }
        let id = self.next_id("m");
        let _ = write!(self.defs, "<clipPath id=\"{}\">{}</clipPath>", id, clip_body);
        let _ = write!(self.body, "<g clip-path=\"url(#{})\">{}</g>", id, content);
        self.bounds.add(visible.x0, visible.y0);
        self.bounds.add(visible.x1, visible.y1);
    }

    /// Rend l'image `frame` (base 1) d'un symbole exporté, comme timeline demandée.
    fn render_symbol(&mut self, id: u16, frame: usize) {
        let age = frame.saturating_sub(1);
        match self.chars.get(&id).copied() {
            Some(Tag::DefineSprite(sp)) => {
                let info = self.sprite_timeline(sp);
                self.group("", |e| e.render_timeline(&sp.tags, info, &M::identity(), 1, age, true));
            }
            _ => self.render_char(id, &M::identity(), None, 0, age, 0),
        }
    }
}

/// Applique `--instance` ou `--sans-instance` à la liste d'affichage d'une timeline (triée par
/// profondeur). Seuls les objets ordinaires sont filtrés par leur nom ; un masque est gardé avec
/// `--sans-instance`, et avec `--instance` s'il découpe l'enfant gardé. Rend aussi si le nom a été vu.
fn filter_instances(items: Vec<(u16, Placed, usize)>, filter: &Instance) -> (Vec<(u16, Placed, usize)>, bool) {
    let matches = |p: &Placed| p.clip_depth.is_none() && p.name.as_deref().is_some_and(|n| n.eq_ignore_ascii_case(filter.name()));
    let found = items.iter().any(|(_, p, _)| matches(p));
    let kept = match filter {
        Instance::Without(_) => items.into_iter().filter(|(_, p, _)| !matches(p)).collect(),
        Instance::Only(_) => {
            let mut kept = Vec::new();
            let mut i = 0;
            while i < items.len() {
                match items[i].1.clip_depth {
                    Some(limit) => {
                        let mut j = i + 1;
                        while j < items.len() && items[j].0 <= limit {
                            j += 1;
                        }
                        let inside: Vec<(u16, Placed, usize)> = items[i + 1..j].iter().filter(|(_, p, _)| matches(p)).cloned().collect();
                        if !inside.is_empty() {
                            kept.push(items[i].clone());
                            kept.extend(inside);
                        }
                        i = j;
                    }
                    None => {
                        if matches(&items[i].1) {
                            kept.push(items[i].clone());
                        }
                        i += 1;
                    }
                }
            }
            kept
        }
    };
    (kept, found)
}

/// Image affichée (base 0) d'une timeline `age` images après sa création, et si ses clips
/// continuent de vieillir sans être recréés (timeline d'une seule image ou arrêtée).
fn play_position(info: Timeline, age: usize, root: bool) -> (usize, bool) {
    if !root {
        if let Some(stop) = info.stop {
            return if age >= stop { (stop, true) } else { (age, false) };
        }
    }
    if info.frames <= 1 {
        (0, true)
    } else {
        (age % info.frames, false)
    }
}

/// Âge d'un clip créé à l'image `born` de sa timeline parente. Quand la parente boucle, les clips
/// posés dès la première image sont conservés d'un tour à l'autre, les autres sont recréés.
fn child_age(age: usize, frame: usize, persistent: bool, born: usize) -> usize {
    if persistent {
        age.saturating_sub(born)
    } else if born == 0 {
        age
    } else {
        frame.saturating_sub(born)
    }
}

/// Lit une timeline : nombre d'images (`ShowFrame`) et première image contenant un `stop()`.
fn timeline_info(tags: &[Tag]) -> Timeline {
    let mut frames = 0usize;
    let mut stop = None;
    for t in tags {
        match t {
            Tag::ShowFrame => frames += 1,
            Tag::DoAction(code) if stop.is_none() && has_stop(code) => stop = Some(frames),
            _ => {}
        }
    }
    let frames = frames.max(1);
    Timeline { frames, stop: stop.filter(|s| *s < frames) }
}

/// Vrai si un bloc AVM1 contient l'action `Stop` (0x07) hors des corps de fonctions. Les
/// conditions ne sont pas évaluées : un `stop()` conditionnel compte comme un arrêt.
fn has_stop(code: &[u8]) -> bool {
    let mut i = 0usize;
    while i < code.len() {
        let op = code[i];
        match op {
            0x00 => return false,
            0x07 => return true,
            _ if op >= 0x80 => {
                if i + 2 >= code.len() {
                    return false;
                }
                let len = u16::from_le_bytes([code[i + 1], code[i + 2]]) as usize;
                let next = i + 3 + len;
                // DefineFunction (0x9B) et DefineFunction2 (0x8E) : le corps suit l'en-tête et sa
                // taille est le dernier mot de l'en-tête ; il ne s'exécute pas avec l'image.
                let body = if (op == 0x9B || op == 0x8E) && len >= 2 && next <= code.len() { u16::from_le_bytes([code[next - 2], code[next - 1]]) as usize } else { 0 };
                i = next + body;
            }
            _ => i += 1,
        }
    }
    false
}

/// Valeur de la pile AVM1 suivie par [`script_end`].
#[derive(Clone, Debug)]
enum Val {
    Str(String),
    Num(f64),
    Other,
}

/// Lit les valeurs d'une action `Push` (types 0 à 9 du format SWF).
fn push_values(payload: &[u8], pool: &[String], stack: &mut Vec<Val>) {
    let mut i = 0usize;
    while i < payload.len() {
        let kind = payload[i];
        i += 1;
        let rest = &payload[i..];
        let (value, size) = match kind {
            0 => {
                let end = rest.iter().position(|b| *b == 0).unwrap_or(rest.len());
                (Val::Str(String::from_utf8_lossy(&rest[..end]).to_string()), end + 1)
            }
            1 if rest.len() >= 4 => (Val::Num(f32::from_le_bytes([rest[0], rest[1], rest[2], rest[3]]) as f64), 4),
            2 | 3 => (Val::Other, 0),
            4 | 5 if !rest.is_empty() => (Val::Other, 1),
            // Double AVM1 : les deux mots de 32 bits sont échangés.
            6 if rest.len() >= 8 => (Val::Num(f64::from_le_bytes([rest[4], rest[5], rest[6], rest[7], rest[0], rest[1], rest[2], rest[3]])), 8),
            7 if rest.len() >= 4 => (Val::Num(i32::from_le_bytes([rest[0], rest[1], rest[2], rest[3]]) as f64), 4),
            8 if !rest.is_empty() => (pool.get(rest[0] as usize).map(|s| Val::Str(s.clone())).unwrap_or(Val::Other), 1),
            9 if rest.len() >= 2 => (pool.get(u16::from_le_bytes([rest[0], rest[1]]) as usize).map(|s| Val::Str(s.clone())).unwrap_or(Val::Other), 2),
            _ => return,
        };
        stack.push(value);
        i += size;
    }
}

/// Script d'arrêt d'un bloc AVM1 (une image) : `GAC.applyAnim(this, "<anim>")` (retour au repos ou
/// animation suivante), `removeMovieClip` (le clip disparaît), sinon `stop()`. La pile n'est suivie
/// que pour les actions utiles (`ConstantPool`, `Push`, `GetVariable`, `GetMember`, appels, `Pop`).
/// Un appel placé après un branchement conditionnel (`If`) ne compte pas : `static<O>` n'enchaîne
/// sur `anim18End` qu'après `anim18`. Un `stop()` compte toujours, comme pour [`has_stop`], et les
/// corps de fonctions sont sautés.
fn script_end(code: &[u8]) -> Option<End> {
    let mut pool: Vec<String> = Vec::new();
    let mut stack: Vec<Val> = Vec::new();
    let mut stop = false;
    let mut conditional = false;
    let mut i = 0usize;
    while i < code.len() {
        let op = code[i];
        if op == 0x00 {
            break;
        }
        if op < 0x80 {
            match op {
                0x07 => stop = true,
                0x17 => {
                    stack.pop();
                }
                0x1C => {
                    stack.pop();
                    stack.push(Val::Other);
                }
                0x4E => {
                    stack.pop();
                    stack.pop();
                    stack.push(Val::Other);
                }
                // RemoveSprite : removeMovieClip(cible).
                0x25 if !conditional => return Some(End::Static),
                // CallFunction, CallMethod : nom, (objet), nombre d'arguments, arguments dans l'ordre.
                0x3D | 0x52 => {
                    let name = match stack.pop() {
                        Some(Val::Str(s)) => Some(s),
                        _ => None,
                    };
                    if op == 0x52 {
                        stack.pop();
                    }
                    let count = match stack.pop() {
                        Some(Val::Num(n)) if (0.0..64.0).contains(&n) => n as usize,
                        _ => 0,
                    };
                    let args: Vec<Val> = (0..count).map(|_| stack.pop().unwrap_or(Val::Other)).collect();
                    if let Some(name) = name.filter(|_| !conditional) {
                        if name.eq_ignore_ascii_case("applyAnim") {
                            if let Some(Val::Str(anim)) = args.get(1) {
                                // Le client ajoute la lettre d'orientation au nom : « StaticR » donne
                                // « staticRR », absent, d'où la pose static<lettre> : retour au repos.
                                let rest = anim.get(..6).filter(|p| p.eq_ignore_ascii_case("static")).map(|_| &anim[6..]);
                                return Some(match rest {
                                    Some(r) if r.is_empty() || r.chars().all(|c| "SRLFB".contains(c)) => End::Static,
                                    _ => End::Next(anim.clone()),
                                });
                            }
                        } else if name.eq_ignore_ascii_case("removeMovieClip") {
                            return Some(End::Static);
                        }
                    }
                    stack.push(Val::Other);
                }
                _ => {}
            }
            i += 1;
            continue;
        }
        if i + 2 >= code.len() {
            break;
        }
        let len = u16::from_le_bytes([code[i + 1], code[i + 2]]) as usize;
        let next = (i + 3 + len).min(code.len());
        let payload = &code[i + 3..next];
        let mut body = 0usize;
        match op {
            0x88 if payload.len() >= 2 => {
                let count = u16::from_le_bytes([payload[0], payload[1]]) as usize;
                pool = payload[2..].split(|b| *b == 0).take(count).map(|s| String::from_utf8_lossy(s).to_string()).collect();
            }
            0x96 => push_values(payload, &pool, &mut stack),
            // If : la condition est retirée ; ce qui suit peut ne pas s'exécuter.
            0x9D => {
                stack.pop();
                conditional = true;
            }
            // DefineFunction et DefineFunction2 : le corps ne s'exécute pas avec l'image.
            0x9B | 0x8E if payload.len() >= 2 => body = u16::from_le_bytes([payload[payload.len() - 2], payload[payload.len() - 1]]) as usize,
            _ => {}
        }
        i = next + body;
    }
    if stop {
        Some(End::Stop)
    } else {
        None
    }
}

/// Document SVG d'un rendu : le cadre `bounds` est arrondi au pixel (le PNG commence au point
/// (⌊xmin⌋, ⌊ymin⌋) du symbole).
fn svg_document(defs: &str, body: &str, bounds: &Bounds) -> String {
    let (x0, y0, x1, y1) = if bounds.set { (bounds.x0.floor(), bounds.y0.floor(), bounds.x1.ceil(), bounds.y1.ceil()) } else { (0.0, 0.0, 1.0, 1.0) };
    let (w, h) = ((x1 - x0).max(1.0), (y1 - y0).max(1.0));
    format!("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{}\" height=\"{}\" viewBox=\"{} {} {} {}\"><defs>{}</defs>{}</svg>", fm(w), fm(h), fm(x0), fm(y0), fm(w), fm(h), defs, body)
}

/// Forme intermédiaire d'une forme morphée (`DefineMorphShape`) à la position `ratio`
/// (0 = forme de départ, 65535 = forme d'arrivée), comme le lecteur Flash : les arêtes de départ
/// et d'arrivée se correspondent une à une, une droite face à une courbe devient une courbe.
fn morph_frame(morph: &swf::DefineMorphShape, ratio: u16) -> swf::Shape {
    use swf::{Point, PointDelta, Twips};
    let t = ratio as f64 / 65535.0;
    let lerp = |a: f64, b: f64| a + (b - a) * t;
    let fill_styles = morph.start.fill_styles.iter().zip(morph.end.fill_styles.iter()).map(|(a, b)| lerp_fill(a, b, t)).collect();
    let line_styles = morph
        .start
        .line_styles
        .iter()
        .zip(morph.end.line_styles.iter())
        .map(|(a, b)| swf::LineStyle::new().with_width(Twips::new(lerp(a.width().get() as f64, b.width().get() as f64).round() as i32)).with_fill_style(lerp_fill(a.fill_style(), b.fill_style(), t)))
        .collect();

    // Positions absolues (twips) des deux plumes, et position émise (arrondie) de la forme produite.
    let (mut s, mut e) = ((0.0f64, 0.0f64), (0.0f64, 0.0f64));
    let mut out = (0i32, 0i32);
    let mut records = Vec::new();
    let mut start = morph.start.shape.iter().peekable();
    let mut end = morph.end.shape.iter().peekable();
    let point = |p: &Point<Twips>| (p.x.get() as f64, p.y.get() as f64);
    let mid = |a: (f64, f64), b: (f64, f64)| ((lerp(a.0, b.0)).round() as i32, (lerp(a.1, b.1)).round() as i32);
    // Arête en coordonnées absolues : (contrôle, ancre, droite ?) ; une droite a son contrôle au milieu.
    let edge = |r: &ShapeRecord, from: (f64, f64)| -> ((f64, f64), (f64, f64), bool) {
        match r {
            ShapeRecord::StraightEdge { delta } => {
                let to = (from.0 + delta.dx.get() as f64, from.1 + delta.dy.get() as f64);
                (((from.0 + to.0) / 2.0, (from.1 + to.1) / 2.0), to, true)
            }
            ShapeRecord::CurvedEdge { control_delta, anchor_delta } => {
                let c = (from.0 + control_delta.dx.get() as f64, from.1 + control_delta.dy.get() as f64);
                ((c), (c.0 + anchor_delta.dx.get() as f64, c.1 + anchor_delta.dy.get() as f64), false)
            }
            ShapeRecord::StyleChange(_) => (from, from, true),
        }
    };
    while let (Some(sr), Some(er)) = (start.peek().copied(), end.peek().copied()) {
        match (sr, er) {
            (ShapeRecord::StyleChange(sc), _) => {
                start.next();
                let mut moved = false;
                if let Some(p) = &sc.move_to {
                    s = point(p);
                    moved = true;
                }
                // Le changement correspondant de la forme d'arrivée ne porte qu'un déplacement.
                if let ShapeRecord::StyleChange(ec) = er {
                    end.next();
                    if let Some(p) = &ec.move_to {
                        e = point(p);
                        moved = true;
                    }
                }
                let mut change = (**sc).clone();
                change.new_styles = None;
                change.move_to = None;
                if moved {
                    out = mid(s, e);
                    change.move_to = Some(Point::new(Twips::new(out.0), Twips::new(out.1)));
                }
                records.push(ShapeRecord::StyleChange(Box::new(change)));
            }
            (_, ShapeRecord::StyleChange(ec)) => {
                end.next();
                if let Some(p) = &ec.move_to {
                    e = point(p);
                    out = mid(s, e);
                    let change = swf::StyleChangeData { move_to: Some(Point::new(Twips::new(out.0), Twips::new(out.1))), fill_style_0: None, fill_style_1: None, line_style: None, new_styles: None };
                    records.push(ShapeRecord::StyleChange(Box::new(change)));
                }
            }
            (se, ee) => {
                start.next();
                end.next();
                let (sc, sa, s_straight) = edge(se, s);
                let (ec, ea, e_straight) = edge(ee, e);
                let anchor = mid(sa, ea);
                if s_straight && e_straight {
                    records.push(ShapeRecord::StraightEdge { delta: PointDelta::new(Twips::new(anchor.0 - out.0), Twips::new(anchor.1 - out.1)) });
                } else {
                    let control = mid(sc, ec);
                    records.push(ShapeRecord::CurvedEdge {
                        control_delta: PointDelta::new(Twips::new(control.0 - out.0), Twips::new(control.1 - out.1)),
                        anchor_delta: PointDelta::new(Twips::new(anchor.0 - control.0), Twips::new(anchor.1 - control.1)),
                    });
                }
                out = anchor;
                s = sa;
                e = ea;
            }
        }
    }
    let rect = |a: &swf::Rectangle<Twips>, b: &swf::Rectangle<Twips>| {
        let l = |x: Twips, y: Twips| Twips::new(lerp(x.get() as f64, y.get() as f64).round() as i32);
        swf::Rectangle { x_min: l(a.x_min, b.x_min), x_max: l(a.x_max, b.x_max), y_min: l(a.y_min, b.y_min), y_max: l(a.y_max, b.y_max) }
    };
    swf::Shape {
        version: 3,
        id: morph.id,
        shape_bounds: rect(&morph.start.shape_bounds, &morph.end.shape_bounds),
        edge_bounds: rect(&morph.start.edge_bounds, &morph.end.edge_bounds),
        flags: swf::ShapeFlag::empty(),
        styles: swf::ShapeStyles { fill_styles, line_styles },
        shape: records,
    }
}

fn lerp_color(a: &swf::Color, b: &swf::Color, t: f64) -> swf::Color {
    let l = |x: u8, y: u8| (x as f64 + (y as f64 - x as f64) * t).round().clamp(0.0, 255.0) as u8;
    swf::Color { r: l(a.r, b.r), g: l(a.g, b.g), b: l(a.b, b.b), a: l(a.a, b.a) }
}

fn lerp_matrix(a: &Matrix, b: &Matrix, t: f64) -> Matrix {
    let f = |x: swf::Fixed16, y: swf::Fixed16| swf::Fixed16::from_f64(x.to_f64() + (y.to_f64() - x.to_f64()) * t);
    let w = |x: swf::Twips, y: swf::Twips| swf::Twips::new((x.get() as f64 + (y.get() as f64 - x.get() as f64) * t).round() as i32);
    Matrix { a: f(a.a, b.a), b: f(a.b, b.b), c: f(a.c, b.c), d: f(a.d, b.d), tx: w(a.tx, b.tx), ty: w(a.ty, b.ty) }
}

fn lerp_gradient(a: &swf::Gradient, b: &swf::Gradient, t: f64) -> swf::Gradient {
    let records = a
        .records
        .iter()
        .zip(b.records.iter())
        .map(|(x, y)| swf::GradientRecord { ratio: (x.ratio as f64 + (y.ratio as f64 - x.ratio as f64) * t).round().clamp(0.0, 255.0) as u8, color: lerp_color(&x.color, &y.color, t) })
        .collect();
    swf::Gradient { matrix: lerp_matrix(&a.matrix, &b.matrix, t), spread: a.spread, interpolation: a.interpolation, records }
}

/// Style de remplissage intermédiaire ; deux styles de natures différentes gardent celui de départ.
fn lerp_fill(a: &FillStyle, b: &FillStyle, t: f64) -> FillStyle {
    match (a, b) {
        (FillStyle::Color(x), FillStyle::Color(y)) => FillStyle::Color(lerp_color(x, y, t)),
        (FillStyle::LinearGradient(x), FillStyle::LinearGradient(y)) => FillStyle::LinearGradient(lerp_gradient(x, y, t)),
        (FillStyle::RadialGradient(x), FillStyle::RadialGradient(y)) => FillStyle::RadialGradient(lerp_gradient(x, y, t)),
        (FillStyle::FocalGradient { gradient: x, focal_point: fx }, FillStyle::FocalGradient { gradient: y, focal_point: fy }) => {
            FillStyle::FocalGradient { gradient: lerp_gradient(x, y, t), focal_point: swf::Fixed8::from_f64(fx.to_f64() + (fy.to_f64() - fx.to_f64()) * t) }
        }
        (FillStyle::Bitmap { id, matrix: x, is_smoothed, is_repeating }, FillStyle::Bitmap { matrix: y, .. }) => FillStyle::Bitmap { id: *id, matrix: lerp_matrix(x, y, t), is_smoothed: *is_smoothed, is_repeating: *is_repeating },
        _ => a.clone(),
    }
}

fn decode_jpeg(data: &[u8]) -> Option<image::RgbaImage> {
    // Les JPEG Flash enchaînent souvent un segment de tables et l'image : « FF D9 FF D8 » (EOI puis SOI)
    // apparaît au début ou au milieu des données et arrête les décodeurs classiques. On retire ces
    // paires ; la jonction JPEGTables + DefineBits se corrige de la même façon.
    let mut d = Vec::with_capacity(data.len());
    let mut i = 0;
    while i < data.len() {
        if i + 3 < data.len() && data[i] == 0xff && data[i + 1] == 0xd9 && data[i + 2] == 0xff && data[i + 3] == 0xd8 {
            i += 4;
            continue;
        }
        d.push(data[i]);
        i += 1;
    }
    image::load_from_memory_with_format(&d, image::ImageFormat::Jpeg).ok().map(|i| i.to_rgba8())
}

fn safe_name(n: &str) -> String {
    n.chars().map(|c| if c.is_ascii_alphanumeric() || c == '_' || c == '-' { c } else { '_' }).collect()
}

/// Images demandées par `--frame`.
#[derive(Clone, Copy, Debug, PartialEq)]
enum Frames {
    /// Une image : le fichier garde le nom du symbole (`walkR.svg`).
    One(usize),
    /// Plusieurs images : un fichier par image (`walkR_f001.svg`…), toutes dans le même cadre.
    Range(usize, usize),
    /// Toutes les images utiles du symbole (voir `--list`).
    All,
}

#[derive(Debug, PartialEq)]
struct Options {
    scene: bool,
    list: bool,
    append_index: bool,
    frames: Frames,
    /// Nom du rendu de la scène (`--name`) ; par défaut, le nom du fichier SWF.
    scene_name: Option<String>,
    /// Enfant seul ou exclu de la timeline demandée (`--instance`, `--sans-instance`).
    instance: Option<Instance>,
    file: String,
    dir: Option<String>,
    names: Vec<String>,
}

fn parse_frames(v: &str) -> Result<Frames, String> {
    let number = |s: &str| -> Result<usize, String> {
        match s.trim().parse::<usize>() {
            Ok(n) if n >= 1 => Ok(n),
            _ => Err(format!("--frame attend un numéro d'image à partir de 1, « {} » reçu", v)),
        }
    };
    if v == "all" {
        return Ok(Frames::All);
    }
    if let Some((a, b)) = v.split_once('-') {
        let (a, b) = (number(a)?, number(b)?);
        if a > b {
            return Err(format!("--frame {} : la première image dépasse la dernière", v));
        }
        return Ok(if a == b { Frames::One(a) } else { Frames::Range(a, b) });
    }
    Ok(Frames::One(number(v)?))
}

fn parse_args(args: &[String]) -> Result<Options, String> {
    let mut o = Options { scene: false, list: false, append_index: false, frames: Frames::One(1), scene_name: None, instance: None, file: String::new(), dir: None, names: Vec::new() };
    let mut instances = 0;
    let mut positional = Vec::new();
    let mut i = 0;
    while i < args.len() {
        let a = &args[i];
        match a.as_str() {
            "--scene" => o.scene = true,
            "--list" => o.list = true,
            "--append-index" => o.append_index = true,
            "--frame" => {
                i += 1;
                let v = args.get(i).ok_or("--frame attend une valeur (N, A-B ou all)")?;
                o.frames = parse_frames(v)?;
            }
            "--name" => {
                i += 1;
                let v = args.get(i).ok_or("--name attend un nom")?;
                o.scene_name = Some(v.clone());
            }
            "--instance" | "--sans-instance" => {
                i += 1;
                let v = args.get(i).ok_or_else(|| format!("{} attend un nom d'instance", a))?;
                o.instance = Some(if a == "--instance" { Instance::Only(v.clone()) } else { Instance::Without(v.clone()) });
                instances += 1;
            }
            "-h" | "--help" => return Err(String::new()),
            _ if a.starts_with("--frame=") => o.frames = parse_frames(&a["--frame=".len()..])?,
            _ if a.starts_with("--name=") => o.scene_name = Some(a["--name=".len()..].to_string()),
            _ if a.starts_with("--instance=") => {
                o.instance = Some(Instance::Only(a["--instance=".len()..].to_string()));
                instances += 1;
            }
            _ if a.starts_with("--sans-instance=") => {
                o.instance = Some(Instance::Without(a["--sans-instance=".len()..].to_string()));
                instances += 1;
            }
            _ if a.starts_with("--") => return Err(format!("option inconnue : {}", a)),
            _ => positional.push(a.clone()),
        }
        i += 1;
    }
    if instances > 1 {
        return Err("--instance et --sans-instance s'emploient une seule fois, l'un ou l'autre".into());
    }
    if o.instance.as_ref().is_some_and(|f| f.name().trim().is_empty()) {
        return Err("--instance et --sans-instance attendent un nom d'instance non vide".into());
    }
    let mut positional = positional.into_iter();
    o.file = positional.next().ok_or("fichier SWF manquant")?;
    if o.list && o.instance.is_some() {
        return Err("--list ne rend rien : --instance et --sans-instance n'y ont pas de sens".into());
    }
    if o.list {
        if positional.next().is_some() {
            return Err("--list n'attend que le fichier SWF".into());
        }
        return Ok(o);
    }
    o.dir = Some(positional.next().ok_or("dossier de sortie manquant")?);
    o.names = positional.collect();
    if o.scene && !o.names.is_empty() {
        return Err("--scene rend la timeline principale : aucun nom d'export n'est attendu".into());
    }
    if o.scene_name.as_deref().is_some_and(|n| n.trim().is_empty()) || (o.scene_name.is_some() && !o.scene) {
        return Err("--name nomme le rendu de --scene et ne peut pas être vide".into());
    }
    Ok(o)
}

/// Ce que l'on rend : un symbole exporté, ou la scène (timeline principale).
struct Target {
    name: String,
    id: Option<u16>,
}

fn file_stem(path: &str) -> String {
    std::path::Path::new(path).file_stem().map(|s| s.to_string_lossy().to_string()).unwrap_or_else(|| "scene".to_string())
}

fn load(path: &str) -> Result<swf::SwfBuf, String> {
    let file = File::open(path).map_err(|e| format!("{} : ouverture impossible ({})", path, e))?;
    swf::decompress_swf(file).map_err(|e| format!("{} : SWF illisible ({})", path, e))
}

fn run(o: &Options) -> Result<String, String> {
    let buf = load(&o.file)?;
    let movie = swf::parse_swf(&buf).map_err(|e| format!("{} : SWF illisible ({})", o.file, e))?;
    let mut exports: Vec<(u16, String)> = Vec::new();
    for t in &movie.tags {
        if let Tag::ExportAssets(list) = t {
            for a in list {
                exports.push((a.id, a.name.to_str_lossy(ENC).to_string()));
            }
        }
    }
    let mut exporter = Exporter::new(&movie.tags);
    exporter.instance = o.instance.clone();
    let main_info = timeline_info(&movie.tags);

    if o.list {
        let mut out = String::from("nom\tid\ttype\timages\timages_timeline\tfin\n");
        let scene_cycle = exporter.cycle_of_timeline(&movie.tags, main_info, true, 0);
        let scene_end = exporter.end_of_timeline(&movie.tags, main_info, true, 0);
        let _ = writeln!(out, "scene\t0\tscene\t{}\t{}\t{}", scene_cycle, main_info.frames, scene_end.label());
        for (id, name) in &exports {
            let (cycle, own, end) = match exporter.chars.get(id).copied() {
                Some(Tag::DefineSprite(sp)) => {
                    let info = exporter.sprite_timeline(sp);
                    (exporter.cycle_of_timeline(&sp.tags, info, true, 0), info.frames, exporter.end_of_timeline(&sp.tags, info, true, 0))
                }
                _ => (exporter.cycle(*id, 0), 1, exporter.end_of(*id, 0)),
            };
            let _ = writeln!(out, "{}\t{}\t{}\t{}\t{}\t{}", name, id, exporter.kind(*id), cycle, own, end.label());
        }
        return Ok(out);
    }

    let dir = o.dir.as_deref().unwrap_or(".");
    std::fs::create_dir_all(dir).map_err(|e| format!("{} : création du dossier impossible ({})", dir, e))?;
    let targets: Vec<Target> = if o.scene {
        vec![Target { name: o.scene_name.clone().unwrap_or_else(|| file_stem(&o.file)), id: None }]
    } else {
        exports.iter().filter(|(_, n)| o.names.is_empty() || o.names.iter().any(|w| w == n)).map(|(id, n)| Target { name: n.clone(), id: Some(*id) }).collect()
    };
    let mut messages = String::new();
    let missing: Vec<&String> = o.names.iter().filter(|w| !exports.iter().any(|(_, n)| n == *w)).collect();
    if !missing.is_empty() {
        let _ = writeln!(messages, "symboles absents de {} : {}", o.file, missing.iter().map(|s| s.as_str()).collect::<Vec<_>>().join(" "));
    }

    let mut index = String::new();
    for target in &targets {
        exporter.instance_found = false;
        let total = match target.id {
            None => exporter.cycle_of_timeline(&movie.tags, main_info, true, 0),
            Some(id) => match exporter.chars.get(&id).copied() {
                Some(Tag::DefineSprite(sp)) => {
                    let info = exporter.sprite_timeline(sp);
                    exporter.cycle_of_timeline(&sp.tags, info, true, 0)
                }
                _ => exporter.cycle(id, 0),
            },
        };
        let (frames, several): (Vec<usize>, bool) = match o.frames {
            Frames::One(n) => (vec![n], false),
            Frames::Range(a, b) => ((a..=b).collect(), true),
            Frames::All => ((1..=total).collect(), true),
        };
        // Rendu de chaque image ; plusieurs images partagent le même cadre (union) pour que les PNG
        // aient la même taille et la même ancre.
        let mut renders: Vec<(usize, String, String, Bounds, Vec<String>)> = Vec::new();
        let mut union = Bounds::default();
        for &n in &frames {
            exporter.reset();
            match target.id {
                None => exporter.render_timeline(&movie.tags, main_info, &M::identity(), 0, n - 1, true),
                Some(id) => exporter.render_symbol(id, n),
            }
            union = union.union(&exporter.bounds);
            renders.push((n, std::mem::take(&mut exporter.defs), std::mem::take(&mut exporter.body), exporter.bounds, std::mem::take(&mut exporter.warnings)));
        }
        for (n, defs, body, bounds, warnings) in renders {
            let b = if several { union } else { bounds };
            let file = if several { format!("{}_f{:03}.svg", safe_name(&target.name), n) } else { format!("{}.svg", safe_name(&target.name)) };
            let path = std::path::Path::new(dir).join(&file);
            std::fs::write(&path, svg_document(&defs, &body, &b)).map_err(|e| format!("{} : écriture impossible ({})", path.display(), e))?;
            let _ = writeln!(index, "{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}", target.name, target.id.unwrap_or(0), fm(b.x0), fm(b.y0), fm(b.x1 - b.x0), fm(b.y1 - b.y0), warnings.join("; "), fm(b.x1), fm(b.y1), n, total, file);
        }
        if let Some(filter) = &o.instance {
            if !exporter.instance_found {
                let _ = writeln!(messages, "instance {} absente de {} ({})", filter.name(), target.name, o.file);
            }
        }
    }
    let index_path = std::path::Path::new(dir).join("index.tsv");
    let mut f = std::fs::OpenOptions::new()
        .create(true)
        .write(true)
        .append(o.append_index)
        .truncate(!o.append_index)
        .open(&index_path)
        .map_err(|e| format!("{} : écriture impossible ({})", index_path.display(), e))?;
    f.write_all(index.as_bytes()).map_err(|e| format!("{} : écriture impossible ({})", index_path.display(), e))?;
    Ok(messages)
}

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let options = match parse_args(&args) {
        Ok(o) => o,
        Err(message) => {
            if !message.is_empty() {
                eprintln!("swfsvg : {}", message);
            }
            eprintln!("{}", USAGE);
            std::process::exit(2);
        }
    };
    match run(&options) {
        Ok(out) => {
            if options.list {
                print!("{}", out);
            } else if !out.is_empty() {
                eprint!("{}", out);
            }
        }
        Err(message) => {
            eprintln!("swfsvg : {}", message);
            std::process::exit(1);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(v: &[&str]) -> Vec<String> {
        v.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn small_gradient_matrices_stay_invertible() {
        // Coefficients d'un dégradé du client (items/115) : arrondis à 0,001 ils devenaient nuls.
        let m = M { a: -0.0021, b: 0.0019, c: 0.00042, d: 0.00038, tx: 288.1244, ty: -0.0004 };
        assert_eq!(m.svg(), "matrix(-0.0021 0.0019 0.00042 0.00038 288.124 0)");
        assert_eq!(fm(2.5), "2.5");
        assert_eq!(fm_fine(-0.0000001), "0");
    }

    #[test]
    fn frames_are_parsed() {
        assert_eq!(parse_frames("3"), Ok(Frames::One(3)));
        assert_eq!(parse_frames("2-5"), Ok(Frames::Range(2, 5)));
        assert_eq!(parse_frames("4-4"), Ok(Frames::One(4)));
        assert_eq!(parse_frames("all"), Ok(Frames::All));
        assert!(parse_frames("0").is_err());
        assert!(parse_frames("5-2").is_err());
        assert!(parse_frames("x").is_err());
    }

    #[test]
    fn options_keep_the_historical_form() {
        let o = parse_args(&args(&["a.swf", "out", "staticR", "staticL"])).unwrap();
        assert_eq!((o.scene, o.list, o.frames), (false, false, Frames::One(1)));
        assert_eq!(o.names, vec!["staticR", "staticL"]);
        let o = parse_args(&args(&["--scene", "--frame=2", "a.swf", "out"])).unwrap();
        assert!(o.scene && o.frames == Frames::One(2));
        assert!(parse_args(&args(&["--scene", "a.swf", "out", "x"])).is_err());
        assert!(parse_args(&args(&["--list", "a.swf", "out"])).is_err());
        assert!(parse_args(&args(&["--bogus", "a.swf", "out"])).is_err());
        assert!(parse_args(&args(&["a.swf"])).is_err());
        let o = parse_args(&args(&["--scene", "--name", "16_1234", "a.swf", "out"])).unwrap();
        assert_eq!(o.scene_name.as_deref(), Some("16_1234"));
        assert!(parse_args(&args(&["--name", "x", "a.swf", "out"])).is_err());
        let o = parse_args(&args(&["--scene", "--instance", "rotate", "a.swf", "out"])).unwrap();
        assert_eq!(o.instance, Some(Instance::Only("rotate".into())));
        let o = parse_args(&args(&["--scene", "--sans-instance=rotate", "a.swf", "out"])).unwrap();
        assert_eq!(o.instance, Some(Instance::Without("rotate".into())));
        assert!(parse_args(&args(&["--instance", "a", "--sans-instance", "b", "a.swf", "out"])).is_err());
        assert!(parse_args(&args(&["--scene", "--instance=", "a.swf", "out"])).is_err());
        assert!(parse_args(&args(&["--list", "--instance", "rotate", "a.swf"])).is_err());
        assert!(parse_args(&args(&["--scene", "a.swf", "out", "--instance"])).is_err());
    }

    #[test]
    fn stop_is_found_outside_function_bodies() {
        assert!(has_stop(&[0x07, 0x00]));
        assert!(!has_stop(&[0x06, 0x00])); // Play
        // Push "a" puis Stop
        assert!(has_stop(&[0x96, 0x03, 0x00, 0x00, b'a', 0x00, 0x07, 0x00]));
        // DefineFunction « f » sans paramètre, corps de 1 octet = Stop : ne compte pas.
        assert!(!has_stop(&[0x9B, 0x06, 0x00, b'f', 0x00, 0x00, 0x00, 0x01, 0x00, 0x07, 0x00]));
        // Bloc tronqué : pas de panique.
        assert!(!has_stop(&[0x96, 0x10]));
    }

    #[test]
    fn nested_clips_play_like_the_client() {
        let looping = Timeline { frames: 26, stop: None };
        assert_eq!(play_position(looping, 30, false), (4, false));
        let stopped = Timeline { frames: 10, stop: Some(8) };
        assert_eq!(play_position(stopped, 3, false), (3, false));
        assert_eq!(play_position(stopped, 20, false), (8, true));
        // La timeline demandée ignore ses stop() : son image est choisie directement.
        assert_eq!(play_position(Timeline { frames: 3, stop: Some(0) }, 1, true), (1, false));
        assert_eq!(play_position(Timeline { frames: 1, stop: None }, 7, true), (0, true));
        assert_eq!(child_age(7, 0, true, 0), 7);
        assert_eq!(child_age(30, 4, false, 0), 30);
        assert_eq!(child_age(30, 4, false, 2), 2);
    }
}
