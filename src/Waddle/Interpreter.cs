using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Waddle
{
    public class Interpreter : CSharpSyntaxVisitor<Task>
    {
        private readonly SemanticModel _semanticModel;
        private readonly Stack _stack;
        private readonly Context _context;

        public Interpreter(SemanticModel semanticModel, Stack stack, Context context)
        {
            _semanticModel = semanticModel;
            _stack = stack;
            _context = context;
        }

        public override async Task VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            await base.Visit(node.Body);

            // If the method is async, wrap the result type in a Task.
            var methodSymbol = _semanticModel.GetDeclaredSymbol(node);
            if (methodSymbol.IsAsync && methodSymbol.ReturnType is INamedTypeSymbol returnTypeSymbol)
            {
                if (returnTypeSymbol.Arity > 0)
                {
                    _stack.Push(Task.FromResult(_stack.Pop()));
                }
                else
                {
                    _stack.Push(Task.CompletedTask);
                }
            }
        }

        public override async Task VisitBlock(BlockSyntax node)
        {
            foreach (var statement in node.Statements)
            {
                await Visit(statement);
            }
        }

        public override async Task VisitIfStatement(IfStatementSyntax node)
        {
            await base.Visit(node.Condition);

            if ((bool)_stack.Pop())
            {
                await base.Visit(node.Statement);
            }
            else
            {
                await base.Visit(node.Else);
            }
        }

        public override async Task VisitWhileStatement(WhileStatementSyntax node)
        {
            while (true)
            {
                await base.Visit(node.Condition);

                if (!(bool)_stack.Pop())
                {
                    return;
                }

                await base.Visit(node.Statement);
            }
        }

        public override async Task VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            await Visit(node.Expression);

            // TODO: Pop stack if expression does not consume last stack value
            var expressionType = _semanticModel.GetTypeInfo(node.Expression);
            if (expressionType.Type.SpecialType != SpecialType.System_Void)
            {
                _stack.Pop();
            }
        }

        public override Task VisitReturnStatement(ReturnStatementSyntax node)
        {
            return Visit(node.Expression);
        }

        public override async Task VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            foreach (var variable in node.Variables)
            {
                await base.Visit(variable);
            }
        }

        public override async Task VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            await base.Visit(node.Initializer.Value);

            _context.SetLocal(node.Identifier.ValueText, _stack.Pop());
        }

        public override async Task VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            await Visit(node.Expression);

            var result = _stack.Pop();
            if (result is Task taskResult)
            {
                // If the stack has a result, push it to the stack.
                var expressionTypeInfo = _semanticModel.GetTypeInfo(node.Expression);
                if (expressionTypeInfo.Type is INamedTypeSymbol namedExpressionType && namedExpressionType.Arity == 1)
                {
                    _stack.Push(await (Task<object>)result);
                }
                else
                {
                    await taskResult;
                }
            }
            else if (result is YieldAwaitable yieldResult)
            {
                await yieldResult;
            }
        }

        public override async Task VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            foreach (var argument in node.ArgumentList.Arguments)
            {
                await Visit(argument.Expression);
            }

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

                // Prepare the parameters.
                var parameters = new List<object>(methodSymbol.Parameters.Length);
                for (var i = 0; i < methodSymbol.Parameters.Length; i++)
                {
                    parameters.Add(_stack.Pop());
                }

                parameters.Reverse();

                // Call the method
                object instance = null;
                if (!methodInfo.IsStatic) throw new Exception("Not supported yet...");
                var result = methodInfo.Invoke(instance, parameters.ToArray());

                if (!methodSymbol.ReturnsVoid)
                {
                    _stack.Push(result);
                }
            }
            else
            {
                // Let the interpreter handle the method call.
                await _context.Call(methodSymbol);
            }
        }

        public override async Task VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            await base.VisitBinaryExpression(node);

            var a = _stack.Pop();
            var b = _stack.Pop();

            switch (node.Kind())
            {
                case SyntaxKind.AddExpression:
                    _stack.Push((int)a + (int)b);
                    break;

                case SyntaxKind.EqualsExpression:
                    _stack.Push(a == b);
                    break;

                default:
                    throw new Exception($"Unknown binary expression kind: {node.Kind()}");
            }
        }

        public override Task VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            _stack.Push(node.Token.Value);

            return Task.CompletedTask;
        }

        public override Task VisitIdentifierName(IdentifierNameSyntax node)
        {
            // The interpreter syntax walker visits an IndentifierNameSyntax node
            // only if the node represents a local that should be pushed onto the stack.
            _stack.Push(_context.GetLocal(node.Identifier.ValueText));

            return Task.CompletedTask;
        }
    }
}
