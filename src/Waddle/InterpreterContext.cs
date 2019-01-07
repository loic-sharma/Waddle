using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Waddle
{
    public class InterpreterContext
    {
        private readonly Workspace _workspace;
        private LatestState _latestState;

        private readonly Stack _stack;
        private readonly Dictionary<string, object> _locals; // TODO: actual stack frames

        public InterpreterContext(Workspace workspace)
        {
            _workspace = workspace;
            _stack = new Stack();
            _locals = new Dictionary<string, object>();
        }

        private async Task BuildSolutionContextAsync(Solution solution, CancellationToken cancellationToken = default)
        {
            // TODO: Skip if this is already the latest solution.
            var project = solution.Projects.First();
            var compilation = await project.GetCompilationAsync(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

            var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);
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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await BuildSolutionContextAsync(_workspace.CurrentSolution, cancellationToken);

            _workspace.WorkspaceChanged += OnWorkspaceChanged;

            var entryPoint = await FindEntryPointSyntaxNodeAsync(cancellationToken);
            entryPoint.Accept(_latestState.Interpreter);
        }

        public object GetLocal(string name)
        {
            return _locals[name];
        }

        public void SetLocal(string name, object value)
        {
            _locals[name] = value;
        }

        public void Call(IMethodSymbol symbol)
        {
            // TODO: Use the inputted symbol if the state hasn't been reloaded.
            var state = _latestState;
            var candidates = state.SemanticModel.Compilation.GetSymbolsWithName(symbol.Name, SymbolFilter.Member);
            IMethodSymbol newSymbol = null;

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

            // Prepare the method's parameters.
            foreach (var parameter in newSymbol.Parameters.Reverse())
            {
                _locals[parameter.Name] = _stack.Pop();
            }

            var syntax = (CSharpSyntaxNode)newSymbol
                .DeclaringSyntaxReferences
                .First()
                .GetSyntax();

            syntax.Accept(state.Interpreter);
        }

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            BuildSolutionContextAsync(e.NewSolution)
                .GetAwaiter()
                .GetResult();
        }

        private async Task<CSharpSyntaxNode> FindEntryPointSyntaxNodeAsync(CancellationToken cancellationToken)
        {
            var compilation = (CSharpCompilation)await _workspace
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
                .Single()
                .GetCompilationAsync(cancellationToken);

            return (CSharpSyntaxNode)compilation
                .GetEntryPoint(cancellationToken)
                .DeclaringSyntaxReferences
                .First()
                .GetSyntax();
        }

        private class LatestState
        {
            public SemanticModel SemanticModel;
            public Interpreter Interpreter;
        }
    }
}
