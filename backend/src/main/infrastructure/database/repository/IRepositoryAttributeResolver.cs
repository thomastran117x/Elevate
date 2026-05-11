using System.Reflection;

namespace backend.main.infrastructure.database.repository
{
    /// Resolves repository method behavior from attributes with a clear precedence order:
    /// interface method → implementation method → implementation class → interface type (first found wins per attribute).
    /// Use this to avoid ambiguity when attributes appear at multiple levels.
    public interface IRepositoryAttributeResolver
    {
        RepositoryMethodBehavior GetBehavior(
            MethodInfo interfaceMethod,
            MethodInfo implementationMethod,
            Type implementationType
        );
    }
}
