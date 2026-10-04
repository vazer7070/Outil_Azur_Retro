//! Modes de swfsvg sur un SWF fabriqué ici (aucun fichier du client n'est nécessaire) :
//! scène, image N, liste, cadre commun des séries, index, masques, formes morphées, erreurs.
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use swf::*;

fn px(v: f64) -> Twips {
    Twips::from_pixels(v)
}

fn rect_records(x0: f64, y0: f64, x1: f64, y1: f64, fill: Option<u32>) -> Vec<ShapeRecord> {
    vec![
        ShapeRecord::StyleChange(Box::new(StyleChangeData { move_to: Some(Point::new(px(x0), px(y0))), fill_style_0: None, fill_style_1: fill, line_style: None, new_styles: None })),
        ShapeRecord::StraightEdge { delta: PointDelta::new(px(x1 - x0), px(0.0)) },
        ShapeRecord::StraightEdge { delta: PointDelta::new(px(0.0), px(y1 - y0)) },
        ShapeRecord::StraightEdge { delta: PointDelta::new(px(x0 - x1), px(0.0)) },
        ShapeRecord::StraightEdge { delta: PointDelta::new(px(0.0), px(y0 - y1)) },
    ]
}

fn bounds(x0: f64, y0: f64, x1: f64, y1: f64) -> Rectangle<Twips> {
    Rectangle { x_min: px(x0), x_max: px(x1), y_min: px(y0), y_max: px(y1) }
}

/// Rectangle plein de `w`×`h` pixels dont le coin haut-gauche est l'origine du symbole.
fn rect(id: u16, w: f64, h: f64, r: u8, g: u8, b: u8) -> Tag<'static> {
    Tag::DefineShape(Shape {
        version: 1,
        id,
        shape_bounds: bounds(0.0, 0.0, w, h),
        edge_bounds: bounds(0.0, 0.0, w, h),
        flags: ShapeFlag::empty(),
        styles: ShapeStyles { fill_styles: vec![FillStyle::Color(Color { r, g, b, a: 255 })], line_styles: vec![] },
        shape: rect_records(0.0, 0.0, w, h, Some(1)),
    })
}

fn place(depth: u16, action: PlaceObjectAction, at: Option<(f64, f64)>) -> PlaceObject<'static> {
    PlaceObject {
        version: 2,
        action,
        depth,
        matrix: at.map(|(x, y)| Matrix::translate(px(x), px(y))),
        color_transform: None,
        ratio: None,
        name: None,
        clip_depth: None,
        class_name: None,
        filters: None,
        background_color: None,
        blend_mode: None,
        clip_actions: None,
        has_image: false,
        is_bitmap_cached: None,
        is_visible: None,
        amf_data: None,
    }
}

fn tag(p: PlaceObject<'static>) -> Tag<'static> {
    Tag::PlaceObject(Box::new(p))
}

fn sprite(id: u16, tags: Vec<Tag<'static>>) -> Tag<'static> {
    let frames = tags.iter().filter(|t| matches!(t, Tag::ShowFrame)).count() as u16;
    Tag::DefineSprite(Sprite { id, num_frames: frames, tags })
}

const STOP: &[u8] = &[0x07, 0x00];

/// SWF de test :
/// - scène : rectangle rouge 20×10 posé en (100, 50) ;
/// - `anim` (3 images) : rouge en x = 0, puis déplacé en x = 30, puis remplacé par un carré bleu ;
/// - `walkR` : clip d'une image contenant `anim` (comme les cycles de marche du client) ;
/// - `etats` (5 images, `stop()` à la première) et `etat`, clip d'une image qui le contient ;
/// - `masque` : carré 40×40 découpé par un masque 10×10 ;
/// - `morph` : forme morphée de 10 à 30 pixels de large posée à mi-chemin (ratio 32768).
fn write_test_swf(path: &Path) {
    let morph_start = rect_records(0.0, 0.0, 10.0, 10.0, Some(1));
    let morph = DefineMorphShape {
        version: 1,
        id: 30,
        flags: DefineMorphShapeFlag::empty(),
        start: MorphShape {
            shape_bounds: bounds(0.0, 0.0, 10.0, 10.0),
            edge_bounds: bounds(0.0, 0.0, 10.0, 10.0),
            fill_styles: vec![FillStyle::Color(Color { r: 0, g: 128, b: 0, a: 255 })],
            line_styles: vec![],
            shape: morph_start,
        },
        end: MorphShape {
            shape_bounds: bounds(0.0, 0.0, 30.0, 10.0),
            edge_bounds: bounds(0.0, 0.0, 30.0, 10.0),
            fill_styles: vec![FillStyle::Color(Color { r: 0, g: 255, b: 0, a: 255 })],
            line_styles: vec![],
            shape: rect_records(0.0, 0.0, 30.0, 10.0, None),
        },
    };
    let mut mask = place(1, PlaceObjectAction::Place(2), Some((0.0, 0.0)));
    mask.clip_depth = Some(2);
    let mut morph_place = place(1, PlaceObjectAction::Place(30), Some((0.0, 0.0)));
    morph_place.ratio = Some(32768);
    let tags = vec![
        rect(1, 20.0, 10.0, 255, 0, 0),
        rect(2, 10.0, 10.0, 0, 0, 255),
        rect(3, 40.0, 40.0, 0, 0, 0),
        sprite(
            10,
            vec![
                tag(place(1, PlaceObjectAction::Place(1), Some((0.0, 0.0)))),
                Tag::ShowFrame,
                tag(place(1, PlaceObjectAction::Modify, Some((30.0, 0.0)))),
                Tag::ShowFrame,
                tag(place(1, PlaceObjectAction::Replace(2), None)),
                Tag::ShowFrame,
            ],
        ),
        sprite(11, vec![tag(place(1, PlaceObjectAction::Place(10), Some((0.0, 0.0)))), Tag::ShowFrame]),
        sprite(
            12,
            vec![
                Tag::DoAction(STOP),
                tag(place(1, PlaceObjectAction::Place(1), None)),
                Tag::ShowFrame,
                tag(place(1, PlaceObjectAction::Modify, Some((5.0, 0.0)))),
                Tag::ShowFrame,
                Tag::ShowFrame,
                Tag::ShowFrame,
                Tag::ShowFrame,
            ],
        ),
        sprite(13, vec![tag(place(1, PlaceObjectAction::Place(12), None)), Tag::ShowFrame]),
        sprite(14, vec![tag(mask), tag(place(2, PlaceObjectAction::Place(3), Some((0.0, 0.0)))), Tag::ShowFrame]),
        Tag::DefineMorphShape(Box::new(morph)),
        sprite(15, vec![tag(morph_place), Tag::ShowFrame]),
        Tag::ExportAssets(vec![
            ExportedAsset { id: 10, name: SwfStr::from_utf8_str("anim") },
            ExportedAsset { id: 11, name: SwfStr::from_utf8_str("walkR") },
            ExportedAsset { id: 12, name: SwfStr::from_utf8_str("etats") },
            ExportedAsset { id: 13, name: SwfStr::from_utf8_str("etat") },
            ExportedAsset { id: 14, name: SwfStr::from_utf8_str("masque") },
            ExportedAsset { id: 15, name: SwfStr::from_utf8_str("morph") },
        ]),
        tag(place(1, PlaceObjectAction::Place(1), Some((100.0, 50.0)))),
        Tag::ShowFrame,
    ];
    let header = Header { compression: Compression::None, version: 8, stage_size: bounds(0.0, 0.0, 550.0, 400.0), frame_rate: Fixed8::from_f32(12.0), num_frames: 1 };
    let mut out = Vec::new();
    write_swf(&header, &tags, &mut out).expect("écriture du SWF de test");
    std::fs::write(path, out).expect("SWF de test");
}

/// Dossier temporaire propre à un test, avec le SWF de test `test.swf`.
struct Work {
    dir: PathBuf,
}

impl Work {
    fn new(name: &str) -> Work {
        let dir = std::env::temp_dir().join(format!("swfsvg-test-{}-{}", std::process::id(), name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        write_test_swf(&dir.join("test.swf"));
        Work { dir }
    }
    fn swf(&self) -> String {
        self.dir.join("test.swf").to_string_lossy().to_string()
    }
    fn out(&self) -> String {
        self.dir.join("out").to_string_lossy().to_string()
    }
    fn read(&self, file: &str) -> String {
        std::fs::read_to_string(self.dir.join("out").join(file)).unwrap_or_else(|e| panic!("{} : {}", file, e))
    }
    /// Lignes de `index.tsv`, découpées en colonnes.
    fn index(&self) -> Vec<Vec<String>> {
        self.read("index.tsv").lines().map(|l| l.split('\t').map(String::from).collect()).collect()
    }
}

impl Drop for Work {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.dir);
    }
}

fn swfsvg(args: &[&str]) -> Output {
    Command::new(env!("CARGO_BIN_EXE_swfsvg")).args(args).output().expect("lancement de swfsvg")
}

fn ok(args: &[&str]) -> String {
    let o = swfsvg(args);
    assert!(o.status.success(), "swfsvg {:?} : {}", args, String::from_utf8_lossy(&o.stderr));
    String::from_utf8_lossy(&o.stdout).to_string()
}

fn row<'a>(index: &'a [Vec<String>], name: &str, frame: &str) -> &'a Vec<String> {
    index.iter().find(|r| r[0] == name && r[9] == frame).unwrap_or_else(|| panic!("{} image {} absent de l'index : {:?}", name, frame, index))
}

fn view_box(svg: &str) -> String {
    let start = svg.find("viewBox=\"").expect("viewBox") + 9;
    svg[start..start + svg[start..].find('"').unwrap()].to_string()
}

#[test]
fn scene_renders_the_main_timeline() {
    let w = Work::new("scene");
    ok(&["--scene", &w.swf(), &w.out()]);
    let svg = w.read("test.svg");
    assert!(svg.contains("<path") && svg.contains("#ff0000"), "scène vide : {}", svg);
    assert_eq!(view_box(&svg), "100 50 20 10");
    let index = w.index();
    assert_eq!(index.len(), 1);
    // nom, id, xmin, ymin, largeur, hauteur, avertissements, xmax, ymax, image, images, fichier
    assert_eq!(index[0], vec!["test", "0", "100", "50", "20", "10", "", "120", "60", "1", "1", "test.svg"]);
    // --name remplace le nom du fichier SWF.
    ok(&["--scene", "--name", "16_1234", &w.swf(), &w.out()]);
    assert!(w.read("16_1234.svg").contains("#ff0000"));
    assert_eq!(w.index()[0][0], "16_1234");
}

#[test]
fn frame_selects_an_image_of_an_exported_symbol() {
    let w = Work::new("frame");
    ok(&["--frame", "1", &w.swf(), &w.out(), "anim"]);
    let first = w.read("anim.svg");
    let first_index = w.index();
    ok(&["--frame", "2", &w.swf(), &w.out(), "anim"]);
    let second = w.read("anim.svg");
    assert_ne!(first, second, "--frame 2 doit différer de --frame 1");
    assert_eq!(row(&first_index, "anim", "1")[2], "0");
    assert_eq!(row(&w.index(), "anim", "2")[2], "30");
    ok(&["--frame=3", &w.swf(), &w.out(), "anim"]);
    assert!(w.read("anim.svg").contains("#0000ff"), "image 3 : carré bleu");
}

#[test]
fn nested_cycles_play_like_the_client() {
    // walkR n'a qu'une image : son clip intérieur joue (cas des cycles de marche du client).
    let w = Work::new("nested");
    ok(&["--frame", "2", &w.swf(), &w.out(), "walkR"]);
    assert_eq!(row(&w.index(), "walkR", "2")[2], "30");
    ok(&["--frame", "4", &w.swf(), &w.out(), "walkR"]);
    assert_eq!(row(&w.index(), "walkR", "4")[2], "0", "le cycle boucle après 3 images");
    // Un clip imbriqué arrêté par stop() reste sur sa première image.
    ok(&["--frame", "2", &w.swf(), &w.out(), "etat"]);
    assert_eq!(row(&w.index(), "etat", "2")[2], "0");
    // Demandé directement, le même clip affiche l'image voulue (comme gotoAndStop).
    ok(&["--frame", "2", &w.swf(), &w.out(), "etats"]);
    assert_eq!(row(&w.index(), "etats", "2")[2], "5");
}

#[test]
fn list_reports_symbols_and_frame_counts_without_rendering() {
    let w = Work::new("list");
    let out = ok(&["--list", &w.swf()]);
    let rows: Vec<Vec<&str>> = out.lines().map(|l| l.split('\t').collect()).collect();
    assert_eq!(rows[0], vec!["nom", "id", "type", "images", "images_timeline"]);
    let find = |n: &str| rows.iter().find(|r| r[0] == n).unwrap_or_else(|| panic!("{} absent : {}", n, out)).clone();
    assert_eq!(find("scene"), vec!["scene", "0", "scene", "1", "1"]);
    assert_eq!(find("anim"), vec!["anim", "10", "clip", "3", "3"]);
    assert_eq!(find("walkR"), vec!["walkR", "11", "clip", "3", "1"]);
    assert_eq!(find("etats"), vec!["etats", "12", "clip", "5", "5"]);
    assert_eq!(find("etat"), vec!["etat", "13", "clip", "1", "1"]);
    assert!(!w.dir.join("out").exists(), "--list ne doit rien écrire");
}

#[test]
fn several_frames_share_one_frame_box() {
    let w = Work::new("all");
    ok(&["--frame", "all", &w.swf(), &w.out(), "walkR"]);
    let boxes: Vec<String> = (1..=3).map(|n| view_box(&w.read(&format!("walkR_f{:03}.svg", n)))).collect();
    assert!(boxes.iter().all(|b| b == &boxes[0]), "cadres différents : {:?}", boxes);
    assert_eq!(boxes[0], "0 0 50 10");
    let index = w.index();
    assert_eq!(index.len(), 3);
    for (n, r) in index.iter().enumerate() {
        assert_eq!((r[9].as_str(), r[10].as_str()), ((n + 1).to_string().as_str(), "3"));
        assert_eq!(r[11], format!("walkR_f{:03}.svg", n + 1));
    }
    ok(&["--frame", "2-3", &w.swf(), &w.out(), "anim"]);
    assert_eq!(w.index().len(), 2);
}

#[test]
fn historical_mode_is_unchanged_and_index_can_be_appended() {
    let w = Work::new("index");
    ok(&[&w.swf(), &w.out()]);
    let index = w.index();
    assert_eq!(index.len(), 6, "un SVG par export, image 1");
    for r in &index {
        assert_eq!(r.len(), 12);
        assert_eq!(r[9], "1");
        assert!(w.dir.join("out").join(&r[11]).exists());
    }
    assert_eq!(&row(&index, "anim", "1")[..7], &["anim", "10", "0", "0", "20", "10", ""]);
    ok(&["--append-index", "--scene", &w.swf(), &w.out()]);
    assert_eq!(w.index().len(), 7);
    ok(&[&w.swf(), &w.out(), "anim"]);
    assert_eq!(w.index().len(), 1, "sans --append-index, l'index est réécrit");
}

#[test]
fn masks_clip_their_content() {
    let w = Work::new("mask");
    ok(&[&w.swf(), &w.out(), "masque"]);
    let svg = w.read("masque.svg");
    assert!(svg.contains("<clipPath") && svg.contains("clip-path=\"url(#"), "masque absent : {}", svg);
    assert_eq!(view_box(&svg), "0 0 10 10", "le cadre est celui de la partie visible");
}

#[test]
fn morph_shapes_are_interpolated() {
    let w = Work::new("morph");
    ok(&[&w.swf(), &w.out(), "morph"]);
    let r = row(&w.index(), "morph", "1").clone();
    assert_eq!((r[4].as_str(), r[6].as_str()), ("20", ""), "mi-chemin entre 10 et 30 pixels : {:?}", r);
    assert!(w.read("morph.svg").contains("#008000") || w.read("morph.svg").contains("#00c000"));
}

#[test]
fn bad_input_is_reported_without_panic() {
    let w = Work::new("errors");
    let junk = w.dir.join("junk.swf");
    std::fs::write(&junk, b"pas un SWF").unwrap();
    for args in [vec![junk.to_string_lossy().to_string(), w.out()], vec![w.dir.join("absent.swf").to_string_lossy().to_string(), w.out()]] {
        let o = swfsvg(&args.iter().map(|s| s.as_str()).collect::<Vec<_>>());
        let err = String::from_utf8_lossy(&o.stderr);
        assert_eq!(o.status.code(), Some(1), "{}", err);
        assert!(err.starts_with("swfsvg : ") && !err.contains("panicked"), "{}", err);
    }
    for args in [vec!["--frame", "0", "a.swf", "out"], vec!["--bogus", "a.swf", "out"], vec!["--scene", "a.swf", "out", "x"], vec![]] {
        assert_eq!(swfsvg(&args).status.code(), Some(2), "{:?}", args);
    }
    // Un nom d'export absent est signalé sans faire échouer la série.
    let o = swfsvg(&[&w.swf(), &w.out(), "anim", "inexistant"]);
    assert!(o.status.success());
    assert!(String::from_utf8_lossy(&o.stderr).contains("inexistant"));
}
