using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Waddle
{
    public static class IMethodSymbolExtension
    {
        public static MethodInfo GetMethodInfoOrNull(this IMethodSymbol symbol)
        {
            var typeName = symbol.ContainingNamespace.Name + "." + symbol.ContainingType.Name;
            var methodName = symbol.Name;
            var methodArgumentTypeNames = symbol
                .Parameters
                .Select(p => p.Type.ContainingNamespace.Name + "." + p.Type.Name)
                .ToList();

            var assembly = typeof(object).Assembly;
            var type = assembly.GetType(typeName);

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
                if (methodInfo.Name != symbol.Name) continue;
                if (!ParameterTypesMatch(methodInfo.GetParameters())) continue;

                return methodInfo;
            }

            return null;
        }
    }
}
