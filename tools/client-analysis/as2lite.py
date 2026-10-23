#!/usr/bin/env python3
"""Pseudo-décompilateur AVM1 : relit la sortie d'avm1dump (ordre de flux) et
reconstruit des expressions lisibles par simulation de pile. Il ne restructure
pas les boucles : les sauts restent visibles sous forme de goto/étiquettes."""
import re, sys, collections

TOKEN = re.compile(r'"(?:[^"\\]|\\.)*"|-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?|r\d+|true|false|null|undefined|NaN|-?inf|cp\d+')
PRELOADS = ["PRELOAD_THIS", "PRELOAD_ARGUMENTS", "PRELOAD_SUPER", "PRELOAD_ROOT", "PRELOAD_PARENT", "PRELOAD_GLOBAL"]
PRELOAD_NAMES = {"PRELOAD_THIS": "this", "PRELOAD_ARGUMENTS": "arguments", "PRELOAD_SUPER": "super", "PRELOAD_ROOT": "_root", "PRELOAD_PARENT": "_parent", "PRELOAD_GLOBAL": "_global"}
BINARY = {"Add2": "+", "Add": "+", "StringAdd": "+", "Subtract": "-", "Multiply": "*", "Divide": "/", "Modulo": "%",
          "Less2": "<", "Less": "<", "Greater": ">", "StringLess": "<", "StringGreater": ">", "Equals2": "==", "Equals": "==", "StringEquals": "==",
          "StrictEquals": "===", "And": "&&", "Or": "||", "BitAnd": "&", "BitOr": "|", "BitXor": "^", "BitLShift": "<<", "BitRShift": ">>", "BitURShift": ">>>",
          "InstanceOf": "instanceof"}
UNARY = {"Not": "!{}", "Increment": "({} + 1)", "Decrement": "({} - 1)", "ToNumber": "Number({})", "ToString": "String({})", "ToInteger": "int({})",
         "TypeOf": "typeof {}", "CharToAscii": "ord({})", "AsciiToChar": "chr({})", "StringLength": "{}.length", "MBStringLength": "{}.length",
         "RandomNumber": "random({})", "MBCharToAscii": "mbord({})", "MBAsciiToChar": "mbchr({})", "Delete2": "delete {}"}


class Node:
    __slots__ = ("off", "op", "args", "nested")
    def __init__(self, off, op, args):
        self.off, self.op, self.args, self.nested = off, op, args, []


def parse_block(lines, i, depth):
    """Lit les lignes d'un bloc à l'indentation donnée ; renvoie (noeuds, index suivant)."""
    nodes = []
    pad = "  " * depth
    while i < len(lines):
        line = lines[i]
        if line.startswith("==="):
            break
        if not line.startswith(pad) or (len(line) > len(pad) and line[len(pad)] == " " and not line.strip().startswith("{")):
            if line.strip() == "}" and len(line) - len(line.lstrip()) < len(pad):
                break
            if len(line) - len(line.lstrip()) < len(pad):
                break
        body = line[len(pad):]
        if body.startswith("{ "):
            head = body[2:]
            inner, i = parse_block(lines, i + 1, depth + 1)
            # ligne fermante
            if i < len(lines) and lines[i].strip() == "}":
                i += 1
            if nodes:
                nodes[-1].nested.append((head, inner))
            continue
        if body == "}":
            break
        m = re.match(r"(::@\d+)|@(\d+) (\S+)(?: (.*))?$", body)
        if m:
            if m.group(1):
                nodes.append(Node(int(m.group(1)[3:]), "LABEL", ""))
            else:
                nodes.append(Node(int(m.group(2)), m.group(3), m.group(4) or ""))
        i += 1
    return nodes, i


def unescape(tok):
    return tok[1:-1]


def lit(tok):
    """Représentation d'un littéral poussé."""
    if tok.startswith('"'):
        return tok
    return tok


class Fn:
    counter = 0
    def __init__(self):
        Fn.counter += 1
        self.id = Fn.counter
        self.header = ""
        self.lines = []


class Decompiler:
    def __init__(self, out):
        self.out = out
        self.functions = {}

    def emit_block(self, nodes, regnames, depth):
        """Simule la pile et renvoie des lignes de pseudo-code."""
        lines = []
        stack = []
        pending_logic = {}  # étiquette -> (op, gauche)
        labels = set()
        for n in nodes:
            m = re.search(r"-> @(\d+)", n.args)
            if n.op in ("If", "Goto") and m and "[toujours pris]" not in n.args and "[jamais pris]" not in n.args:
                labels.add(int(m.group(1)))
        ind = "    " * depth

        def pop():
            return stack.pop() if stack else "<pile vide>"

        def reg(tok):
            return regnames.get(tok, tok)

        def name_of(expr):
            """Nom simple si l'expression est une chaîne littérale identifiant."""
            if expr.startswith('"') and expr.endswith('"'):
                inner = expr[1:-1]
                if re.fullmatch(r"[A-Za-z_$][A-Za-z0-9_$]*", inner):
                    return inner
                return None
            return None

        def member(obj, mem):
            nm = name_of(mem)
            return f"{obj}.{nm}" if nm else f"{obj}[{mem}]"

        def stmt(text):
            lines.append(ind + text)

        def call_args(argc_tok):
            try:
                argc = int(float(argc_tok))
            except ValueError:
                return [f"<argc={argc_tok}>"]
            return [pop() for _ in range(argc)]

        last_dup = None
        i = 0
        while i < len(nodes):
            n = nodes[i]
            op, args = n.op, n.args
            if n.off in labels and op != "LABEL" and n.off != 0:
                # étiquette atteinte en séquence
                if n.off in pending_logic:
                    lop, left = pending_logic.pop(n.off)
                    right = pop()
                    stack.append(f"({left} {lop} {right})")
                elif not (lines and lines[-1].strip() == f"@{n.off}:"):
                    stmt(f"@{n.off}:")
            if op == "LABEL":
                stack = []
                if n.off in labels and not (lines and lines[-1].strip() == f"@{n.off}:"):
                    stmt(f"@{n.off}:")
            elif op == "Push":
                toks = TOKEN.findall(args)
                for t in toks:
                    stack.append(reg(t) if t.startswith("r") and t[1:].isdigit() else lit(t))
            elif op == "ConstantPool":
                pass
            elif op == "GetVariable":
                v = pop(); nm = name_of(v)
                stack.append(nm if nm else f"eval({v})")
            elif op == "SetVariable":
                v = pop(); nm_e = pop(); nm = name_of(nm_e) or f"eval({nm_e})"
                stmt(f"{nm} = {v};")
            elif op == "GetMember":
                mem = pop(); obj = pop(); stack.append(member(obj, mem))
            elif op == "SetMember":
                v = pop(); mem = pop(); obj = pop()
                stmt(f"{member(obj, mem)} = {v};")
            elif op == "CallMethod":
                mem = pop(); obj = pop(); a = call_args(pop())
                if mem in ('""', "undefined"):
                    stack.append(f"{obj}({', '.join(a)})")
                else:
                    stack.append(f"{member(obj, mem)}({', '.join(a)})")
            elif op == "CallFunction":
                fn = pop(); a = call_args(pop()); nm = name_of(fn) or f"eval({fn})"
                stack.append(f"{nm}({', '.join(a)})")
            elif op == "NewObject":
                fn = pop(); a = call_args(pop()); nm = name_of(fn) or fn
                stack.append(f"new {nm}({', '.join(a)})")
            elif op == "NewMethod":
                mem = pop(); obj = pop(); a = call_args(pop())
                target = member(obj, mem) if mem != '""' else obj
                stack.append(f"new {target}({', '.join(a)})")
            elif op == "Pop":
                v = pop()
                if "(" in v or v.startswith("new ") or v.startswith("delete"):
                    stmt(f"{v};")
            elif op == "StoreRegister":
                r = reg(args.strip())
                if stack:
                    stmt(f"{r} = {stack[-1]};")
                    stack[-1] = r
                else:
                    stmt(f"{r} = <pile vide>;")
            elif op == "DefineLocal":
                v = pop(); nm_e = pop(); nm = name_of(nm_e) or nm_e
                stmt(f"var {nm} = {v};")
            elif op == "DefineLocal2":
                nm_e = pop(); nm = name_of(nm_e) or nm_e
                stmt(f"var {nm};")
            elif op in BINARY:
                b = pop(); a = pop(); stack.append(f"({a} {BINARY[op]} {b})")
            elif op in UNARY:
                a = pop(); stack.append(UNARY[op].format(a))
            elif op == "PushDuplicate":
                if stack:
                    stack.append(stack[-1])
                    last_dup = (i, stack[-1])
            elif op == "StackSwap":
                if len(stack) >= 2:
                    stack[-1], stack[-2] = stack[-2], stack[-1]
            elif op == "If":
                m = re.search(r"-> @(\d+)", args); target = int(m.group(1)) if m else -1
                cond = pop()
                # motifs && / || : PushDuplicate [Not] If ; Pop ; rhs ; étiquette
                nxt = nodes[i + 1] if i + 1 < len(nodes) else None
                if last_dup and last_dup[0] >= i - 2 and nxt is not None and nxt.op == "Pop" and stack and stack[-1] == last_dup[1]:
                    if cond == f"!{last_dup[1]}":
                        pending_logic[target] = ("&&", last_dup[1]); stack.pop(); i += 2; last_dup = None; continue
                    if cond == last_dup[1]:
                        pending_logic[target] = ("||", last_dup[1]); stack.pop(); i += 2; last_dup = None; continue
                if "[toujours pris]" in args:
                    pass
                elif "[jamais pris]" in args:
                    pass
                else:
                    stmt(f"if ({cond}) goto @{target};")
            elif op == "Jump":
                pass  # le flux continue à la cible dans le listing
            elif op == "Goto":
                m = re.search(r"-> @(\d+)", args)
                stmt(f"goto @{m.group(1)};" if m else "goto ?;")
                if stack:
                    stmt(f"/* pile : {', '.join(stack)} */")
                stack = []
            elif op == "Return":
                v = pop() if stack else ""
                stmt(f"return {v};".replace("return ;", "return;"))
            elif op == "Throw":
                stmt(f"throw {pop()};")
            elif op == "End":
                pass
            elif op in ("DefineFunction", "DefineFunction2"):
                m = re.match(r'"((?:[^"\\]|\\.)*)" \(([^)]*)\)(?: regs=(\d+) flags=FunctionFlags\(([^)]*)\))?', args)
                fname = m.group(1) if m else "?"
                params_raw = [p.strip() for p in (m.group(2) if m else "").split(",") if p.strip()]
                flags = (m.group(4) or "").split(" | ") if m else []
                sub = {}
                nxt_reg = 1
                for fl in PRELOADS:
                    if fl in flags:
                        sub[f"r{nxt_reg}"] = PRELOAD_NAMES[fl]; nxt_reg += 1
                pnames = []
                for k, p in enumerate(params_raw):
                    if "=" in p:
                        pn, rn = p.rsplit("=", 1)
                    else:
                        pn, rn = p, None
                    if not re.fullmatch(r"[A-Za-z_$][A-Za-z0-9_$]*", pn):
                        pn = f"p{k + 1}"
                    pnames.append(pn)
                    if rn:
                        sub[rn] = pn
                body_nodes = n.nested[0][1] if n.nested else []
                fn = Fn()
                fn.header = f"function {fname}({', '.join(pnames)})"
                fn.lines = self.emit_block(body_nodes, sub, depth + 1)
                self.functions[fn.id] = fn
                stack.append(f"FN#{fn.id}")
            elif op == "With":
                obj = pop()
                stmt(f"with ({obj}) {{")
                for head, inner in n.nested:
                    lines.extend(self.emit_block(inner, regnames, depth + 1))
                stmt("}")
            elif op == "Try":
                stmt("try {")
                for head, inner in n.nested:
                    if head != "Try":
                        stmt(f"}} {head.lower()} {{")
                    lines.extend(self.emit_block(inner, regnames, depth + 1))
                stmt("}")
            elif op == "InitArray":
                a = call_args(pop()); stack.append("[" + ", ".join(a) + "]")
            elif op == "InitObject":
                try:
                    cnt = int(float(pop()))
                except ValueError:
                    cnt = 0
                pairs = []
                for _ in range(cnt):
                    v = pop(); k = pop(); pairs.append(f"{k}: {v}")
                stack.append("{" + ", ".join(reversed(pairs)) + "}")
            elif op == "Extends":
                sup = pop(); sub_ = pop(); stmt(f"{sub_} extends {sup};")
            elif op == "ImplementsOp":
                cnt = call_args(pop()); obj = pop(); stmt(f"{obj} implements {', '.join(cnt)};")
            elif op == "CastOp":
                obj = pop(); typ = pop(); stack.append(f"({typ})({obj})")
            elif op in ("Enumerate2", "Enumerate"):
                obj = pop(); stack.append(f"enumerate({obj})")
            elif op == "GetProperty":
                idx = pop(); tgt = pop(); stack.append(f"getProperty({tgt}, {idx})")
            elif op == "SetProperty":
                v = pop(); idx = pop(); tgt = pop(); stmt(f"setProperty({tgt}, {idx}, {v});")
            elif op == "Delete":
                mem = pop(); obj = pop(); stack.append(f"delete {member(obj, mem)}")
            elif op == "Trace":
                stmt(f"trace({pop()});")
            elif op in ("GetTime",):
                stack.append("getTimer()")
            elif op == "StringExtract" or op == "MBStringExtract":
                cnt = pop(); idx = pop(); st = pop(); stack.append(f"substring({st}, {idx}, {cnt})")
            elif op in ("GetUrl2",):
                tgt = pop(); url = pop(); stmt(f"getURL({url}, {tgt});")
            elif op in ("Play", "Stop", "NextFrame", "PreviousFrame", "StopSounds", "ToggleQuality"):
                stmt(f"{op.lower()}();")
            elif op in ("GotoFrame2",):
                stmt(f"gotoFrame({pop()});")
            elif op in ("SetTarget2",):
                stmt(f"tellTarget({pop()});")
            else:
                stmt(f"/* {op} {args} | pile : {', '.join(stack)} */")
                stack = []
            i += 1
        if stack:
            lines.append(ind + f"/* pile restante : {', '.join(stack)} */")
        return lines

    def expand(self, lines, depth=0):
        """Remplace les marqueurs FN#k par le corps des fonctions."""
        out = []
        for line in lines:
            m = re.search(r"FN#(\d+)", line)
            if not m:
                out.append(line); continue
            fn = self.functions[int(m.group(1))]
            ind = re.match(r"\s*", line).group(0)
            head = line[:m.start()] + fn.header + " {"
            out.append(head)
            out.extend(self.expand(fn.lines, depth + 1))
            tail = line[m.end():]
            out.append(ind + "}" + tail)
        return out


def main():
    src, dst = sys.argv[1], sys.argv[2]
    lines = open(src, encoding="utf-8").read().split("\n")
    exports = {}
    for l in lines:
        m = re.match(r'=== Export (\d+) "(.*)" ===', l)
        if m:
            exports[int(m.group(1))] = m.group(2)
    out = open(dst, "w", encoding="utf-8")
    i = 0
    while i < len(lines):
        l = lines[i]
        m = re.match(r"=== (.*) ===", l)
        if not m:
            i += 1; continue
        head = m.group(1)
        if head.startswith("Export "):
            i += 1; continue
        sm = re.match(r"DoInitAction sprite=(\d+)", head)
        title = head
        if sm:
            title = f"{exports.get(int(sm.group(1)), '?')}  (sprite {sm.group(1)})"
        nodes, i = parse_block(lines, i + 1, 0)
        d = Decompiler(out)
        body = d.expand(d.emit_block(nodes, {}, 1))
        out.write(f"//// {title}\n")
        out.write("\n".join(body) + "\n\n")
    out.close()


if __name__ == "__main__":
    main()
