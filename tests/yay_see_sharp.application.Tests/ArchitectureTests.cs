using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

/// <summary>
/// Enforces, by reflection rather than by discipline, that ViewModels stay dependent only on
/// abstractions: a ViewModel that starts declaring a field or constructor parameter typed as
/// anything from the Infrastructure namespace — concrete class *or* interface — is exactly the
/// shape the "ViewModels new up Infrastructure directly" (FINDING-08) and "an abstraction like
/// IEngineDetector ended up defined in Infrastructure instead of domain" (NEW-07) findings were
/// about. Interfaces are included deliberately: NEW-07 was caught by a field typed as
/// <c>yay_see_sharp.infrastructure.Platform.IEngineDetector</c> — a pure abstraction, so it would
/// have slipped past a concrete-types-only check — before the interface itself moved to
/// <c>yay_see_sharp.domain.Abstractions</c>.
///
/// <see cref="DesignMainWindowViewModel"/> is the one documented, intentional exception: it's the
/// XAML previewer's design-time DataContext, never runs in the shipped app, and its own doc
/// comment explains why it's allowed to construct concrete Infrastructure services directly. Those
/// are local `new` expressions inside its constructor bodies, not fields or constructor
/// parameters, so this reflection-based check — which only inspects the field/parameter *shape* of
/// a type, never method bodies — already leaves it untouched without needing a special case here.
/// </summary>
public class ArchitectureTests
{
    [Test]
    public async Task No_ViewModel_field_or_constructor_parameter_is_typed_as_an_infrastructure_class_or_interface()
    {
        var viewModelTypes = typeof(ViewModelBase).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ViewModelBase).Namespace)
            .ToArray();

        var offenders = new List<string>();

        foreach (var type in viewModelTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (IsInfrastructureType(field.FieldType))
                {
                    offenders.Add($"{type.Name}.{field.Name} : {field.FieldType.FullName}");
                }
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (IsInfrastructureType(parameter.ParameterType))
                    {
                        offenders.Add($"{type.Name}({parameter.Name} : {parameter.ParameterType.FullName})");
                    }
                }
            }
        }

        await Assert.That(offenders).IsEmpty();
    }

    private static bool IsInfrastructureType(Type type) =>
        type.Namespace is { } ns &&
        ns.StartsWith("yay_see_sharp.infrastructure", StringComparison.Ordinal);
}
