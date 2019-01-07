using System;
using System.Collections.Generic;
using System.Linq;
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

                    for (var i = 0; i < parameters.Length; i++)
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
                    for (var i = 0; i < methodSymbol.Parameters.Length; i++)
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
}
