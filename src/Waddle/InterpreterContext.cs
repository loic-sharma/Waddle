using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Waddle
{
    public class InterpreterContext
    {
        private readonly Stack _stack;
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
}
