using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators;

/// <summary>
/// Shared Roslyn type rendering and generic-constraint infrastructure for all emitters.
/// </summary>
internal static class GeneratorTypeSystem
{
    internal static TypeParameterContext CreateTypeParameterContext(INamedTypeSymbol typeSymbol)
    {
        var typeChain = new Stack<INamedTypeSymbol>();
        INamedTypeSymbol? current = typeSymbol;

        while (current is not null)
        {
            typeChain.Push(current);
            current = current.ContainingType;
        }

        var parameters = new List<ITypeParameterSymbol>();
        var names = new Dictionary<ITypeParameterSymbol, string>(SymbolEqualityComparer.Default);
        var index = 0;

        foreach (var type in typeChain)
        {
            foreach (var parameter in type.TypeParameters)
            {
                parameters.Add(parameter);
                names.Add(parameter, "TSource" + index);
                index++;
            }
        }

        return new TypeParameterContext(parameters, names);
    }

    internal static string RenderType(ITypeSymbol typeSymbol, TypeParameterContext typeParameters)
    {
        if (typeSymbol is ITypeParameterSymbol typeParameter &&
            typeParameters.Names.TryGetValue(typeParameter, out var parameterName))
        {
            return parameterName;
        }

        var builder = new StringBuilder();

        foreach (var part in typeSymbol.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            if (part.Symbol is ITypeParameterSymbol partTypeParameter &&
                typeParameters.Names.TryGetValue(partTypeParameter, out var replacement))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(part.ToString());
            }
        }

        return builder.ToString();
    }

    internal static void AppendMethodTypeParameters(
        StringBuilder source,
        TypeParameterContext typeParameters)
    {
        if (typeParameters.Parameters.Count == 0)
        {
            return;
        }

        source.Append('<');

        for (var index = 0; index < typeParameters.Parameters.Count; index++)
        {
            if (index != 0)
            {
                source.Append(", ");
            }

            source.Append(typeParameters.Names[typeParameters.Parameters[index]]);
        }

        source.Append('>');
    }

    internal static void AppendMethodConstraints(
        StringBuilder source,
        TypeParameterContext typeParameters)
    {
        foreach (var parameter in typeParameters.Parameters)
        {
            var constraints = GetConstraints(parameter, typeParameters);

            if (constraints.Count == 0)
            {
                continue;
            }

            source.AppendLine()
                .Append("        where ")
                .Append(typeParameters.Names[parameter])
                .Append(" : ")
                .Append(string.Join(", ", constraints));
        }
    }

    internal static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;

    private static List<string> GetConstraints(
        ITypeParameterSymbol parameter,
        TypeParameterContext typeParameters)
    {
        var constraints = new List<string>();

        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add(
                parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                    ? "class?"
                    : "class");
        }
        else if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        foreach (var constraintType in parameter.ConstraintTypes)
        {
            constraints.Add(RenderType(constraintType, typeParameters));
        }

        if (parameter.HasConstructorConstraint &&
            !parameter.HasValueTypeConstraint &&
            !parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("new()");
        }

        if (parameter.AllowsRefLikeType)
        {
            constraints.Add("allows ref struct");
        }

        return constraints;
    }
}

internal sealed class TypeParameterContext
{
    internal TypeParameterContext(
        IReadOnlyList<ITypeParameterSymbol> parameters,
        IReadOnlyDictionary<ITypeParameterSymbol, string> names)
    {
        Parameters = parameters;
        Names = names;
    }

    internal IReadOnlyList<ITypeParameterSymbol> Parameters { get; }

    internal IReadOnlyDictionary<ITypeParameterSymbol, string> Names { get; }
}
