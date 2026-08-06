using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

/// <summary>
/// Enforces, by reflection rather than by discipline, that ViewModels stay dependent only on
/// abstractions: a ViewModel that starts declaring a field or constructor parameter typed as a
/// concrete Infrastructure class is exactly the shape the "ViewModels new up Infrastructure
/// directly" finding was about — this fails the build before it ships again.
/// </summary>
public class ArchitectureTests
{
    [Test]
    public async Task No_ViewModel_field_or_constructor_parameter_is_typed_as_a_concrete_infrastructure_class()
    {
        var viewModelTypes = typeof(ViewModelBase).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ViewModelBase).Namespace)
            .ToArray();

        var offenders = new List<string>();

        foreach (var type in viewModelTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (IsConcreteInfrastructureType(field.FieldType))
                {
                    offenders.Add($"{type.Name}.{field.Name} : {field.FieldType.FullName}");
                }
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (IsConcreteInfrastructureType(parameter.ParameterType))
                    {
                        offenders.Add($"{type.Name}({parameter.Name} : {parameter.ParameterType.FullName})");
                    }
                }
            }
        }

        await Assert.That(offenders).IsEmpty();
    }

    private static bool IsConcreteInfrastructureType(Type type) =>
        type.Namespace is { } ns &&
        ns.StartsWith("yay_see_sharp.infrastructure", StringComparison.Ordinal) &&
        !type.IsInterface;
}
