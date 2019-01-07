//using Microsoft.CodeAnalysis.CSharp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Waddle
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            var sourceText = SourceText.From(@"
using System;

namespace Test
{
    class Program
    {
        static string Test()
        {
            return ""Hello World"";
        }

        static void Main(string[] args)
        {
/*
            if (4 == (1 + 3))
            {
                Console.WriteLine(""True!"");
            }
*/
            while (true) {
              Console.WriteLine(""Value {0}"", Test());
            }
        }
    }
}
");
            var cancellationToken = CancellationToken.None;
            var workspace = new AdhocWorkspace();

            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "HelloWorld",
                "HelloWorld",
                LanguageNames.CSharp,
                metadataReferences: new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                });

            var project = workspace.AddProject(projectInfo);
            var document = workspace.AddDocument(project.Id, "Program.cs", sourceText);

            var context = new InterpreterContext(workspace);
            
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ModifyWorkspace(workspace, document);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            var compilation = (CSharpCompilation)await workspace
                .CurrentSolution
                .Projects
                .Where(p =>
                {
                    switch (p.CompilationOptions.OutputKind)
                    {
                        case OutputKind.ConsoleApplication:
                        case OutputKind.WindowsApplication:
                        case OutputKind.WindowsRuntimeApplication:
                            return true;

                        default:
                            return false;
                    }
                })
                .First()
                .GetCompilationAsync(cancellationToken);

            var entryPoint = (CSharpSyntaxNode)compilation
                .GetEntryPoint(cancellationToken)
                .DeclaringSyntaxReferences
                .First()
                .GetSyntax();

            entryPoint.Accept(context.LatestInterpreter());
        }

        static async Task ModifyWorkspace(Workspace workspace, Document document)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            var newSourceText = SourceText.From(@"
using System;

namespace Test
{
    class Program
    {
        static string Test(int a)
        {
            return ""Bad"";
        }

        static string Test()
        {
            return ""Foo bar"";
        }

        static void Main(string[] args)
        {
/*
            if (4 == (1 + 3))
            {
                Console.WriteLine(""True!"");
            }
*/
            while (true) {
              Console.WriteLine(Test());
            }
        }
    }
}
");

            var newSolution = workspace.CurrentSolution.WithDocumentText(document.Id, newSourceText);
            workspace.TryApplyChanges(newSolution);
        }
    }

    public class InterpreterContext
    {
        private Stack _stack;
        private LatestState _latestState;

        public InterpreterContext(Workspace workspace)
        {
            _stack = new Stack();
            BuildSolutionContext(workspace.CurrentSolution);

            workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            BuildSolutionContext(e.NewSolution);
        }

        private void BuildSolutionContext(Solution solution)
        {
            // TODO: Skip if this is already the latest solution.
            var project = solution.Projects.First();
            var compilation = project.GetCompilationAsync().Result;
            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

            var diagnostics = semanticModel.GetDiagnostics();
            if (diagnostics.Any())
            {
                // Reject invalid solutions.
                return;
            }

            _latestState = new LatestState
            {
                SemanticModel = semanticModel,
                Interpreter = new Interpreter(semanticModel, _stack, this)
            };
        }

        public void Call(IMethodSymbol symbol)
        {
            // TODO: Use the inputted symbol if the state hasn't been reloaded.
            var state = _latestState;
            var candidates = state.SemanticModel.Compilation.GetSymbolsWithName(symbol.Name, SymbolFilter.Member);
            ISymbol newSymbol = null;

            foreach (var candidate in candidates.Cast<IMethodSymbol>())
            {
                if (symbol.Arity != candidate.Arity) continue;
                if (symbol.Parameters.Length != candidate.Parameters.Length) continue;
                if (symbol.ContainingNamespace.Name != candidate.ContainingNamespace.MetadataName) continue;
                if (symbol.ContainingType.MetadataName != candidate.ContainingType.MetadataName) continue;

                newSymbol = candidate;
                break;
            }

            if (newSymbol == null) throw new Exception();

            var syntax = (CSharpSyntaxNode)newSymbol
                .DeclaringSyntaxReferences
                .First()
                .GetSyntax();

            syntax.Accept(state.Interpreter);
        }

        public Interpreter LatestInterpreter() => _latestState.Interpreter;

        private class LatestState
        {
            public SemanticModel SemanticModel;
            public Interpreter Interpreter;
        }
    }

    public class Interpreter : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly Stack _stack;
        private readonly InterpreterContext _context;

        public Interpreter(SemanticModel semanticModel, Stack stack, InterpreterContext context)
        {
            _semanticModel = semanticModel;
            _stack = stack;
            _context = context;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            base.Visit(node.Condition);

            // TODO: Pop stack if expression statement does not consume last object
            if ((bool)_stack.Pop())
            {
                base.Visit(node.Statement);
            }
            else
            {
                base.Visit(node.Else);
            }
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            while (true)
            {
                base.Visit(node.Condition);

                if (!(bool)_stack.Pop())
                {
                    return;
                }

                base.Visit(node.Statement);
            }
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.VisitExpressionStatement(node);

            // TODO: Pop stack if expression does not consume last stack value
            var expressionType = _semanticModel.GetTypeInfo(node.Expression);
            if (expressionType.Type.SpecialType != SpecialType.System_Void)
            {
                _stack.Pop();
            }
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            base.VisitInvocationExpression(node);

            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var methodSymbol = (IMethodSymbol)symbolInfo.Symbol;

            // Use reflection to call methods from referenced assemblies.
            if (symbolInfo.Symbol.ContainingAssembly.Name == "mscorlib")
            {
                var typeName = methodSymbol.ContainingNamespace.Name + "." + methodSymbol.ContainingType.Name;
                var methodName = methodSymbol.Name;

                var assembly = typeof(object).Assembly;
                var type = assembly.GetType(typeName);

                var methodArgumentTypeNames = methodSymbol
                    .Parameters
                    .Select(p => p.Type.ContainingNamespace.Name + "." + p.Type.Name)
                    .ToList();

                bool ParameterTypesMatch(ParameterInfo[] parameters)
                {
                    if (parameters.Length != methodArgumentTypeNames.Count) return false;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType.FullName != methodArgumentTypeNames[i]) return false;
                    }

                    return true;
                }

                foreach (var methodInfo in type.GetMethods())
                {
                    if (methodInfo.Name != methodSymbol.Name) continue;
                    if (!ParameterTypesMatch(methodInfo.GetParameters())) continue;

                    var parameters = new List<object>(methodSymbol.Parameters.Length);
                    for (int i = 0; i < methodSymbol.Parameters.Length; i++)
                    {
                        parameters.Add(_stack.Pop());
                    }

                    parameters.Reverse();
                    methodInfo.Invoke(null, parameters.ToArray());
                    return;
                }

                throw new Exception("Coudln't find method!");
            }
            
            _context.Call(methodSymbol);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            base.VisitBinaryExpression(node);

            var a = _stack.Pop();
            var b = _stack.Pop();

            switch (node.Kind())
            {
                case SyntaxKind.AddExpression:
                    _stack.Push((int)a + (int)b);
                    break;

                case SyntaxKind.EqualsExpression:
                    _stack.Push((int)a == (int)b);
                    break;

                default:
                    throw new Exception($"Unknown binary expression kind: {node.Kind()}");
            }
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            _stack.Push(node.Token.Value);
        }
    }

    public class Stack
    {
        private readonly List<object> _stack = new List<object>();
        private int _stackIndex = 0;

        public object Pop()
        {
            return _stack[--_stackIndex];
        }

        public void Push(object value)
        {
            if (_stack.Count == _stackIndex)
            {
                _stack.Add(value);
                _stackIndex++;
            }
            else
            {
                _stack[_stackIndex++] = value;
            }
        }
    }
}

/*
 *             /*
            var tree = CSharpSyntaxTree.ParseText(
@"
using System;

namespace Test
{
  class Program
  {
    void Test()
    {
      Console.WriteLine(""Bad"");
    }

    static void Main(string[] args)
    {
      if (4 == (1 + 3)) {
        Console.WriteLine(""True!"");
      }
    }
  }
}
");

            

            var root = (CompilationUnitSyntax)tree.GetRoot();
            var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication, mainTypeName: "Test.Program");

            var compilation = CSharpCompilation
                .Create("HelloWorld", options: options)
                .AddReferences(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);

            //var model = compilation.GetSemanticModel(tree);

            var entryPoint = (CSharpSyntaxNode)compilation
                .GetEntryPoint(cancellationToken)
                .DeclaringSyntaxReferences
                .First()
                .GetSyntax();
            */
