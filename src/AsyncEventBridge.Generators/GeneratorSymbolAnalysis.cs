using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

/// <summary>
/// Shared symbol discovery and accessibility rules used by generator emitters.
/// </summary>
internal static class GeneratorSymbolAnalysis
{
    internal static IEnumerable<IEventSymbol> GetEventsForGeneration(
        INamedTypeSymbol typeSymbol,
        Func<INamedTypeSymbol, bool>? shouldStopAtBaseType)
    {
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            foreach (var eventSymbol in typeSymbol.GetMembers().OfType<IEventSymbol>())
            {
                yield return eventSymbol;
            }

            yield break;
        }

        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        INamedTypeSymbol? current = typeSymbol;
        var isTargetType = true;

        while (current is not null)
        {
            if (!isTargetType && shouldStopAtBaseType is not null && shouldStopAtBaseType(current))
            {
                yield break;
            }

            foreach (var eventSymbol in current.GetMembers().OfType<IEventSymbol>())
            {
                if (!hiddenNames.Contains(eventSymbol.Name))
                {
                    yield return eventSymbol;
                }
            }

            foreach (var member in current.GetMembers())
            {
                hiddenNames.Add(member.Name);
            }

            isTargetType = false;
            current = current.BaseType;
        }
    }

    internal static bool CanAccessEvent(
        IEventSymbol eventSymbol,
        INamedTypeSymbol targetType,
        bool isExternalTarget = false)
    {
        if (eventSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            return true;
        }

        if (isExternalTarget)
        {
            return false;
        }

        var sameAssembly = SymbolEqualityComparer.Default.Equals(
            eventSymbol.ContainingAssembly,
            targetType.ContainingAssembly);

        return sameAssembly &&
            eventSymbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    internal static string GetMethodAccessibility(
        INamedTypeSymbol typeSymbol,
        IEventSymbol eventSymbol,
        bool requirePublicDelegateParameters = false)
    {
        if (!IsPubliclyAccessible(typeSymbol) ||
            eventSymbol.DeclaredAccessibility != Accessibility.Public ||
            !IsPubliclyAccessible(eventSymbol.Type))
        {
            return "internal";
        }

        if (requirePublicDelegateParameters &&
            eventSymbol.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod } &&
            invokeMethod.Parameters.Any(parameter => !IsPubliclyAccessible(parameter.Type)))
        {
            return "internal";
        }

        return "public";
    }

    internal static bool IsPubliclyAccessible(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is ITypeParameterSymbol)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsPubliclyAccessible(arrayType.ElementType);
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return true;
        }

        if (namedType.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (namedType.ContainingType is not null && !IsPubliclyAccessible(namedType.ContainingType))
        {
            return false;
        }

        return namedType.TypeArguments.All(IsPubliclyAccessible);
    }

    internal static bool CanGenerateForType(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Interface))
        {
            return false;
        }

        INamedTypeSymbol? current = typeSymbol;

        while (current is not null)
        {
            if (current.DeclaredAccessibility is not (
                Accessibility.Public or
                Accessibility.Internal or
                Accessibility.ProtectedOrInternal))
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
    }
}
