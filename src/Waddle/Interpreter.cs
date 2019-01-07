using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Waddle
{
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

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            foreach (var variable in node.Variables)
            {
                base.Visit(variable);
            }
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            base.Visit(node.Initializer.Value);

            _context.SetLocal(node.Identifier.ValueText, _stack.Pop());
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            base.Visit(node.ArgumentList);

            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var methodSymbol = (IMethodSymbol)symbolInfo.Symbol;

            // Use reflection to call methods from referenced assemblies.
            if (symbolInfo.Symbol.ContainingAssembly.Name == "mscorlib")
            {
                var methodInfo = methodSymbol.GetMethodInfoOrNull();
                if (methodInfo == null)
                {
                    throw new Exception();
                }

                var parameters = new List<object>(methodSymbol.Parameters.Length);
                for (var i = 0; i < methodSymbol.Parameters.Length; i++)
                {
                    parameters.Add(_stack.Pop());
                }

                object instance = null;
                if (!methodInfo.IsStatic) throw new Exception("Not supported yet...");

                parameters.Reverse();
                methodInfo.Invoke(instance, parameters.ToArray());
            }
            else
            {
                // Let the interpreter handle the method call.
                _context.Call(methodSymbol);
            }
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

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            // The interpreter syntax walker visits an IndentifierNameSyntax node
            // only if the node represents a local that should be pushed onto the stack.
            _stack.Push(_context.GetLocal(node.Identifier.ValueText));
        }
    }
}
