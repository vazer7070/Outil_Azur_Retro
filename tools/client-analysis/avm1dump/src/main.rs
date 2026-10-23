use std::collections::HashSet;
use std::fs::File;
use std::io::Write;
use std::rc::Rc;
use swf::avm1::read::Reader;
use swf::avm1::types::{Action, Value};
use swf::{SwfStr, Tag};

const ENC: &'static encoding_rs::Encoding = encoding_rs::UTF_8;

fn s(x: &SwfStr) -> String {
    let t = x.to_str_lossy(ENC);
    let mut o = String::new();
    for c in t.chars() {
        match c {
            '"' => o.push_str("\\\""),
            '\\' => o.push_str("\\\\"),
            '\n' => o.push_str("\\n"),
            '\r' => o.push_str("\\r"),
            '\t' => o.push_str("\\t"),
            c if (c as u32) < 32 => o.push_str(&format!("\\x{:02x}", c as u32)),
            c => o.push(c),
        }
    }
    o
}

/// Valeur constante connue sur la pile, pour replier les prédicats opaques.
#[derive(Clone, Debug)]
enum Known {
    Str(String),
    Num(f64),
    Bool(bool),
    Nullish,
    Unknown,
}

fn truthy(k: &Known) -> Option<bool> {
    match k {
        Known::Str(t) => Some(!t.is_empty()),
        Known::Num(n) => Some(*n != 0.0 && !n.is_nan()),
        Known::Bool(b) => Some(*b),
        Known::Nullish => Some(false),
        Known::Unknown => None,
    }
}

struct Ctx<'a> {
    out: &'a mut dyn Write,
    version: u8,
}

struct Line {
    text: String,
    nested: Vec<(String, Vec<u8>, Rc<Vec<String>>)>, // (en-tête, corps, pool)
}

fn fmt_value(v: &Value, pool: &[String]) -> String {
    match v {
        Value::Undefined => "undefined".to_string(),
        Value::Null => "null".to_string(),
        Value::Bool(b) => format!("{}", b),
        Value::Int(i) => format!("{}", i),
        Value::Float(f) => format!("{}", f),
        Value::Double(d) => format!("{}", d),
        Value::Str(st) => format!("\"{}\"", s(st)),
        Value::Register(r) => format!("r{}", r),
        Value::ConstantPool(i) => match pool.get(*i as usize) {
            Some(st) => format!("\"{}\"", st),
            None => format!("cp{}", i),
        },
    }
}

fn known_of(v: &Value, pool: &[String]) -> Known {
    match v {
        Value::Undefined | Value::Null => Known::Nullish,
        Value::Bool(b) => Known::Bool(*b),
        Value::Int(i) => Known::Num(*i as f64),
        Value::Float(f) => Known::Num(*f as f64),
        Value::Double(d) => Known::Num(*d),
        Value::Str(st) => Known::Str(st.to_str_lossy(ENC).to_string()),
        Value::ConstantPool(i) => match pool.get(*i as usize) {
            Some(st) => Known::Str(st.clone()),
            None => Known::Unknown,
        },
        Value::Register(_) => Known::Unknown,
    }
}

/// Désassemble un bloc en suivant le flux depuis l'offset 0, dans l'ordre
/// d'exécution ; le code mort placé par l'obfuscateur entre les sauts n'est
/// pas décodé. Les prédicats opaques (getTimer, chaînes constantes…) sont repliés.
fn dump(ctx: &mut Ctx, data: &[u8], indent: usize, pool0: Rc<Vec<String>>) {
    // Première passe : quel est le plus grand ConstantPool atteignable ? C'est
    // celui du compilateur ; les autres sont des leurres de l'obfuscateur.
    let best = largest_pool(data, ctx.version, pool0.clone());
    let fixed = best.is_some();
    let pool0 = best.unwrap_or(pool0);
    let pad = "  ".repeat(indent);
    let mut lines: Vec<Line> = Vec::new();
    let mut seen: HashSet<usize> = HashSet::new();
    let mut work: Vec<(usize, Rc<Vec<String>>, Vec<Known>)> = vec![(0, pool0, Vec::new())];
    while let Some((start, pool, stack)) = work.pop() {
        if seen.contains(&start) {
            continue;
        }
        if start != 0 {
            lines.push(Line { text: format!("{}::@{}", pad, start), nested: vec![] });
        }
        let mut pos = start;
        let mut pool = pool;
        let mut stack = stack;
        loop {
            if pos >= data.len() {
                break;
            }
            if !seen.insert(pos) {
                lines.push(Line { text: format!("{}@{} Goto -> @{}", pad, pos, pos), nested: vec![] });
                break;
            }
            let mut reader = Reader::new(&data[pos..], ctx.version);
            let action = match reader.read_action() {
                Ok(a) => a,
                Err(e) => {
                    lines.push(Line { text: format!("{}@{} <erreur {}>", pad, pos, e), nested: vec![] });
                    break;
                }
            };
            let next = pos + (data.len() - pos - reader.get_mut().len());
            let mut nested = Vec::new();
            let mut stop = false;
            let mut jump_to: Option<usize> = None;
            let text = match action {
                Action::End => {
                    stop = true;
                    format!("{}@{} End", pad, pos)
                }
                Action::ConstantPool(cp) => {
                    let strings: Vec<String> = cp.strings.iter().map(|st| s(st)).collect();
                    let t = format!("{}@{} ConstantPool {}{}", pad, pos, strings.len(), if fixed && strings.len() != pool.len() { "  [ignoré]" } else { "" });
                    if !fixed {
                        pool = Rc::new(strings);
                    }
                    t
                }
                Action::Push(push) => {
                    let parts: Vec<String> = push.values.iter().map(|v| fmt_value(v, &pool)).collect();
                    for v in &push.values {
                        stack.push(known_of(v, &pool));
                    }
                    format!("{}@{} Push {}", pad, pos, parts.join(" ; "))
                }
                Action::CharToAscii => {
                    let k = match stack.pop() {
                        Some(Known::Str(t)) => Known::Num(t.chars().next().map(|c| c as u32 as f64).unwrap_or(f64::NAN)),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                    format!("{}@{} CharToAscii", pad, pos)
                }
                Action::Not => {
                    let k = match stack.pop().as_ref().and_then(truthy) {
                        Some(b) => Known::Bool(!b),
                        None => Known::Unknown,
                    };
                    stack.push(k);
                    format!("{}@{} Not", pad, pos)
                }
                Action::GetTime => {
                    // L'obfuscateur utilise getTimer() comme prédicat « toujours vrai ».
                    stack.push(Known::Num(1.0));
                    format!("{}@{} GetTime", pad, pos)
                }
                Action::Increment | Action::Decrement => {
                    let inc = matches!(action, Action::Increment);
                    let k = match stack.pop() {
                        Some(Known::Num(n)) => Known::Num(if inc { n + 1.0 } else { n - 1.0 }),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                    format!("{}@{} {}", pad, pos, if inc { "Increment" } else { "Decrement" })
                }
                Action::PushDuplicate => {
                    let k = stack.last().cloned().unwrap_or(Known::Unknown);
                    stack.push(k);
                    format!("{}@{} PushDuplicate", pad, pos)
                }
                Action::StrictEquals | Action::Equals2 => {
                    let strict = matches!(action, Action::StrictEquals);
                    let b = stack.pop().unwrap_or(Known::Unknown);
                    let a = stack.pop().unwrap_or(Known::Unknown);
                    let k = match (&a, &b) {
                        (Known::Str(x), Known::Str(y)) => Known::Bool(x == y),
                        (Known::Num(x), Known::Num(y)) => Known::Bool(x == y),
                        (Known::Bool(x), Known::Bool(y)) => Known::Bool(x == y),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                    format!("{}@{} {}", pad, pos, if strict { "StrictEquals" } else { "Equals2" })
                }
                Action::If(i) => {
                    let target = ((next as i64) + (i.offset as i64)).max(0) as usize;
                    match stack.pop().as_ref().and_then(truthy) {
                        Some(true) => {
                            jump_to = Some(target);
                            format!("{}@{} If -> @{}  [toujours pris]", pad, pos, target)
                        }
                        Some(false) => format!("{}@{} If -> @{}  [jamais pris]", pad, pos, target),
                        None => {
                            work.push((target, pool.clone(), stack.clone()));
                            format!("{}@{} If -> @{}", pad, pos, target)
                        }
                    }
                }
                Action::Jump(j) => {
                    let target = ((next as i64) + (j.offset as i64)).max(0) as usize;
                    jump_to = Some(target);
                    format!("{}@{} Jump -> @{}", pad, pos, target)
                }
                Action::Return => {
                    stop = true;
                    format!("{}@{} Return", pad, pos)
                }
                Action::Throw => {
                    stop = true;
                    format!("{}@{} Throw", pad, pos)
                }
                Action::StoreRegister(r) => format!("{}@{} StoreRegister r{}", pad, pos, r.register),
                Action::DefineFunction(f) => {
                    let params: Vec<String> = f.params.iter().map(|p| s(p)).collect();
                    nested.push(("Function".to_string(), f.actions.to_vec(), pool.clone()));
                    stack.push(Known::Unknown);
                    format!("{}@{} DefineFunction \"{}\" ({})", pad, pos, s(f.name), params.join(", "))
                }
                Action::DefineFunction2(f) => {
                    let params: Vec<String> = f
                        .params
                        .iter()
                        .map(|p| match p.register_index {
                            Some(r) => format!("{}=r{}", s(p.name), r.get()),
                            None => s(p.name),
                        })
                        .collect();
                    nested.push(("Function".to_string(), f.actions.to_vec(), pool.clone()));
                    stack.push(Known::Unknown);
                    format!("{}@{} DefineFunction2 \"{}\" ({}) regs={} flags={:?}", pad, pos, s(f.name), params.join(", "), f.register_count, f.flags)
                }
                Action::With(w) => {
                    nested.push(("With".to_string(), w.actions.to_vec(), pool.clone()));
                    stack.clear();
                    format!("{}@{} With", pad, pos)
                }
                Action::Try(t) => {
                    nested.push(("Try".to_string(), t.try_body.to_vec(), pool.clone()));
                    if let Some((var, body)) = &t.catch_body {
                        nested.push((format!("Catch {:?}", var), body.to_vec(), pool.clone()));
                    }
                    if let Some(body) = t.finally_body {
                        nested.push(("Finally".to_string(), body.to_vec(), pool.clone()));
                    }
                    stack.clear();
                    format!("{}@{} Try", pad, pos)
                }
                Action::GotoLabel(g) => format!("{}@{} GotoLabel \"{}\"", pad, pos, s(g.label)),
                Action::SetTarget(t) => format!("{}@{} SetTarget \"{}\"", pad, pos, s(t.target)),
                Action::GetUrl(g) => format!("{}@{} GetUrl \"{}\" \"{}\"", pad, pos, s(g.url), s(g.target)),
                other => {
                    let name = format!("{:?}", other);
                    let name = name.split(|c| c == '(' || c == ' ' || c == '{').next().unwrap_or("?").to_string();
                    // Toute autre opération consomme/produit des valeurs inconnues.
                    stack.clear();
                    format!("{}@{} {}", pad, pos, name)
                }
            };
            lines.push(Line { text, nested });
            if stop {
                break;
            }
            pos = jump_to.unwrap_or(next);
        }
    }
    for line in lines {
        let _ = writeln!(ctx.out, "{}", line.text);
        for (head, body, pool) in line.nested {
            let _ = writeln!(ctx.out, "{}{{ {}", pad, head);
            dump(ctx, &body, indent + 1, pool);
            let _ = writeln!(ctx.out, "{}}}", pad);
        }
    }
}


/// Parcourt le bloc comme `dump` et renvoie le plus grand ConstantPool atteint.
fn largest_pool(data: &[u8], version: u8, pool0: Rc<Vec<String>>) -> Option<Rc<Vec<String>>> {
    let mut candidates: Vec<Rc<Vec<String>>> = vec![pool0.clone()];
    let mut refs: Vec<u16> = Vec::new();
    let mut seen: HashSet<usize> = HashSet::new();
    let mut work: Vec<(usize, Vec<Known>)> = vec![(0, Vec::new())];
    let pool = pool0;
    while let Some((start, stack)) = work.pop() {
        let mut pos = start;
        let mut stack = stack;
        loop {
            if pos >= data.len() || !seen.insert(pos) {
                break;
            }
            let mut reader = Reader::new(&data[pos..], version);
            let Ok(action) = reader.read_action() else { break };
            let next = pos + (data.len() - pos - reader.get_mut().len());
            let mut jump_to = None;
            match action {
                Action::End | Action::Return | Action::Throw => break,
                Action::ConstantPool(cp) => {
                    candidates.push(Rc::new(cp.strings.iter().map(|st| s(st)).collect()));
                }
                Action::Push(push) => {
                    for v in &push.values {
                        if let Value::ConstantPool(i) = v {
                            refs.push(*i);
                        }
                        stack.push(known_of(v, &pool));
                    }
                }
                Action::CharToAscii => {
                    let k = match stack.pop() {
                        Some(Known::Str(t)) => Known::Num(t.chars().next().map(|c| c as u32 as f64).unwrap_or(f64::NAN)),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                }
                Action::Not => {
                    let k = match stack.pop().as_ref().and_then(truthy) {
                        Some(b) => Known::Bool(!b),
                        None => Known::Unknown,
                    };
                    stack.push(k);
                }
                Action::GetTime => stack.push(Known::Num(1.0)),
                Action::Increment | Action::Decrement => {
                    let inc = matches!(action, Action::Increment);
                    let k = match stack.pop() {
                        Some(Known::Num(n)) => Known::Num(if inc { n + 1.0 } else { n - 1.0 }),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                }
                Action::PushDuplicate => {
                    let k = stack.last().cloned().unwrap_or(Known::Unknown);
                    stack.push(k);
                }
                Action::StrictEquals | Action::Equals2 => {
                    let b = stack.pop().unwrap_or(Known::Unknown);
                    let a = stack.pop().unwrap_or(Known::Unknown);
                    let k = match (&a, &b) {
                        (Known::Str(x), Known::Str(y)) => Known::Bool(x == y),
                        (Known::Num(x), Known::Num(y)) => Known::Bool(x == y),
                        (Known::Bool(x), Known::Bool(y)) => Known::Bool(x == y),
                        _ => Known::Unknown,
                    };
                    stack.push(k);
                }
                Action::If(i) => {
                    let target = ((next as i64) + (i.offset as i64)).max(0) as usize;
                    match stack.pop().as_ref().and_then(truthy) {
                        Some(true) => jump_to = Some(target),
                        Some(false) => {}
                        None => work.push((target, stack.clone())),
                    }
                }
                Action::Jump(j) => {
                    jump_to = Some(((next as i64) + (j.offset as i64)).max(0) as usize);
                }
                Action::DefineFunction(_) | Action::DefineFunction2(_) => stack.push(Known::Unknown),
                _ => stack.clear(),
            }
            pos = jump_to.unwrap_or(next);
        }
    }
    if refs.is_empty() || candidates.len() <= 1 {
        return None;
    }
    // Le vrai pool est celui dont les constantes référencées sont lisibles ;
    // les leurres de l'obfuscateur sont faits de caractères de contrôle.
    let score = |p: &Rc<Vec<String>>| -> usize {
        refs.iter()
            .filter(|&&i| p.get(i as usize).map(|st| !st.is_empty() && st.chars().all(|c| (' '..='~').contains(&c) || c as u32 >= 0xa0)).unwrap_or(false))
            .count()
    };
    candidates.into_iter().max_by_key(|p| score(p))
}

fn walk(ctx: &mut Ctx, tags: &[Tag], prefix: &str) {
    for tag in tags {
        match tag {
            Tag::DoAction(data) => {
                let _ = writeln!(ctx.out, "=== {}DoAction ===", prefix);
                dump(ctx, data, 0, Rc::new(Vec::new()));
            }
            Tag::DoInitAction { id, action_data } => {
                let _ = writeln!(ctx.out, "=== {}DoInitAction sprite={} ===", prefix, id);
                dump(ctx, action_data, 0, Rc::new(Vec::new()));
            }
            Tag::DefineSprite(sprite) => {
                walk(ctx, &sprite.tags, &format!("{}Sprite{}/", prefix, sprite.id));
            }
            Tag::ExportAssets(assets) => {
                for a in assets {
                    let _ = writeln!(ctx.out, "=== Export {} \"{}\" ===", a.id, s(a.name));
                }
            }
            _ => {}
        }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 3 {
        eprintln!("usage: avm1dump <entrée.swf> <sortie.txt>");
        std::process::exit(2);
    }
    let file = File::open(&args[1]).expect("ouverture");
    let buf = swf::decompress_swf(file).expect("décompression");
    let swf = swf::parse_swf(&buf).expect("analyse");
    let mut out = std::io::BufWriter::new(File::create(&args[2]).expect("sortie"));
    let version = swf.header.version();
    let mut ctx = Ctx { out: &mut out, version };
    walk(&mut ctx, &swf.tags, "");
}
