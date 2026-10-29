"""FCS Python-subset interpreter. Never exec/eval user code or import user modules.
JSON stdin -> JSON stdout. Bounded expressions/statements, no I/O in user scripts.
"""
import ast
import json
import math
import sys

LIMIT = 4096
ALLOWED = (ast.Module, ast.FunctionDef, ast.arguments, ast.arg, ast.Return, ast.Assign,
           ast.If, ast.For, ast.Expr, ast.Name, ast.Load, ast.Store, ast.Constant,
           ast.List, ast.Tuple, ast.Dict, ast.Subscript, ast.BinOp, ast.UnaryOp,
           ast.BoolOp, ast.Compare, ast.Call, ast.Add, ast.Sub, ast.Mult, ast.Div,
           ast.Mod, ast.USub, ast.UAdd, ast.Not, ast.And, ast.Or, ast.Eq, ast.NotEq,
           ast.Lt, ast.LtE, ast.Gt, ast.GtE, ast.In, ast.NotIn)
CALLS = ('min', 'max', 'sum', 'len', 'abs', 'round', 'range')


def validate(source):
    if not isinstance(source, str) or not 1 <= len(source.encode()) <= 16384:
        raise ValueError('source_size')
    tree = ast.parse(source)
    if len(tree.body) != 1 or not isinstance(tree.body[0], ast.FunctionDef): raise ValueError('one_run_function_required')
    fn = tree.body[0]
    if fn.name != 'run' or fn.decorator_list or fn.returns or fn.type_comment or getattr(fn, 'type_params', []): raise ValueError('invalid_run')
    a = fn.args
    if [x.arg for x in a.args] != ['signals', 'settings'] or a.defaults or a.kw_defaults or a.kwonlyargs or a.posonlyargs or a.vararg or a.kwarg or any(x.annotation for x in a.args): raise ValueError('run_arguments')
    nodes = list(ast.walk(tree))
    if len(nodes) > 2048: raise ValueError('syntax_limit')
    for node in nodes:
        if not isinstance(node, ALLOWED): raise ValueError('syntax_not_allowed:' + type(node).__name__)
        if isinstance(node, ast.FunctionDef) and node is not fn: raise ValueError('nested_function')
        if isinstance(node, ast.Name) and node.id.startswith('_'): raise ValueError('private_name')
        if isinstance(node, ast.Call) and (not isinstance(node.func, ast.Name) or node.func.id not in CALLS or node.keywords): raise ValueError('call_not_allowed')
        if isinstance(node, ast.Assign) and (len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name)): raise ValueError('assignment_target')
        if isinstance(node, ast.For) and (not isinstance(node.target, ast.Name) or node.orelse): raise ValueError('loop_target')
        if isinstance(node, ast.Dict) and any(k is None for k in node.keys): raise ValueError('dictionary_unpack')
        if isinstance(node, ast.Constant): bounded(node.value)
    return fn


def bounded(value, depth=0):
    if depth > 16: raise ValueError('depth_limit')
    if value is None or type(value) is bool: return value
    if type(value) in (int, float):
        if not math.isfinite(value) or abs(value) > 1e15: raise ValueError('number_limit')
    elif isinstance(value, str):
        if len(value) > 4096: raise ValueError('string_limit')
    elif isinstance(value, (list, tuple)):
        if len(value) > 64: raise ValueError('list_limit')
        for item in value: bounded(item, depth+1)
    elif isinstance(value, dict):
        if len(value) > 64 or any(not isinstance(k, str) or len(k)>128 for k in value): raise ValueError('dictionary_limit')
        for item in value.values(): bounded(item, depth+1)
    else: raise ValueError('type_not_allowed')
    return value


class Interpreter:
    def __init__(self, signals, settings):
        self.env = {'signals': bounded(signals), 'settings': bounded(settings)}
        self.remaining = LIMIT

    def step(self):
        self.remaining -= 1
        if self.remaining < 0: raise ValueError('instruction_budget')

    def expr(self, n):
        self.step()
        if isinstance(n, ast.Constant): return bounded(n.value)
        if isinstance(n, ast.Name): return self.env[n.id]
        if isinstance(n, (ast.List, ast.Tuple)): return bounded([self.expr(x) for x in n.elts])
        if isinstance(n, ast.Dict): return bounded({self.expr(k): self.expr(v) for k,v in zip(n.keys,n.values)})
        if isinstance(n, ast.Subscript):
            value, key = self.expr(n.value), self.expr(n.slice)
            if not isinstance(value, (dict,list,tuple,str)) or type(key) not in (str,int): raise ValueError('invalid_subscript')
            return value[key]
        if isinstance(n, ast.UnaryOp):
            v = self.expr(n.operand)
            if isinstance(n.op, ast.Not): return not v
            if type(v) not in (int,float): raise ValueError('number_required')
            return bounded(-v if isinstance(n.op,ast.USub) else +v)
        if isinstance(n, ast.BoolOp):
            value = self.expr(n.values[0])
            for item in n.values[1:]:
                if isinstance(n.op,ast.And) and not value or isinstance(n.op,ast.Or) and value: break
                value = self.expr(item)
            return value
        if isinstance(n, ast.BinOp):
            a,b = self.expr(n.left),self.expr(n.right)
            if type(a) not in (int,float) or type(b) not in (int,float): raise ValueError('numeric_arithmetic_only')
            operations = {ast.Add:lambda:a+b, ast.Sub:lambda:a-b, ast.Mult:lambda:a*b, ast.Div:lambda:a/b, ast.Mod:lambda:a%b}
            return bounded(operations[type(n.op)]())
        if isinstance(n, ast.Compare):
            a = self.expr(n.left)
            for op,other in zip(n.ops,n.comparators):
                b = self.expr(other)
                operations = {ast.Eq:lambda:a==b, ast.NotEq:lambda:a!=b, ast.Lt:lambda:a<b, ast.LtE:lambda:a<=b,
                              ast.Gt:lambda:a>b, ast.GtE:lambda:a>=b, ast.In:lambda:a in b, ast.NotIn:lambda:a not in b}
                if not operations[type(op)](): return False
                a=b
            return True
        if isinstance(n, ast.Call):
            args = [self.expr(x) for x in n.args]
            name = n.func.id
            if name == 'range':
                if not 1 <= len(args) <= 3 or any(type(a) is not int for a in args): raise ValueError('range_arguments')
                value=range(*args)
                if len(value)>64: raise ValueError('range_limit')
                return list(value)
            fn = {'min':min,'max':max,'sum':sum,'len':len,'abs':abs,'round':round}[name]
            return bounded(fn(*args))
        raise ValueError('expression_not_allowed')

    def statements(self, nodes):
        for node in nodes:
            self.step()
            if isinstance(node,ast.Return): return True,self.expr(node.value) if node.value else None
            if isinstance(node,ast.Assign): self.env[node.targets[0].id]=bounded(self.expr(node.value))
            elif isinstance(node,ast.If):
                done,result=self.statements(node.body if self.expr(node.test) else node.orelse)
                if done:return done,result
            elif isinstance(node,ast.For):
                items=self.expr(node.iter)
                if not isinstance(items,(list,tuple,dict,str)) or len(items)>64:raise ValueError('iteration_limit')
                for item in items:
                    self.env[node.target.id]=item
                    done,result=self.statements(node.body)
                    if done:return done,result
            elif isinstance(node,ast.Expr): self.expr(node.value)
            elif not isinstance(node,ast.Return): raise ValueError('statement_not_allowed')
        return False,None


def execute(request):
    fn=validate(request['source'])
    if request.get('mode')=='validate':return {'ok':True,'language':'fcs-python-subset/1'}
    result=Interpreter(request.get('signals',{}),request.get('settings',{})).statements(fn.body)[1]
    if not isinstance(result,list) or len(result)>8 or any(not isinstance(a,dict) for a in result):raise ValueError('return_action_list')
    text=json.dumps(result,allow_nan=False)
    if len(text.encode())>8192:raise ValueError('result_size')
    return {'ok':True,'actions':result}

if __name__=='__main__':
    try:
        raw=sys.stdin.buffer.read(65537)
        if len(raw)>65536:raise ValueError('request_size')
        response=execute(json.loads(raw))
    except Exception as exc:
        response={'ok':False,'error':str(exc)[:160]}
    print(json.dumps(response,allow_nan=False,separators=(',',':')))
