using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace IBeam.Identity.Services.Configuration;

/// <summary>
/// Binds a configuration section onto an options instance so that a collection the configuration
/// specifies REPLACES the instance's default rather than being appended to it (IBM-0073).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConfigurationBinder"/> adds items to whatever a collection property already holds; it
/// never clears it first. That is fine for a property whose default is empty, and silently wrong for a
/// property whose default is not: the operator can widen the list but can never narrow it, and nothing
/// reports that their configuration did not take effect.
/// </para>
/// <para>
/// It matters most where a list is an authorization boundary. Configuring
/// <c>AdminRoleNames: ["RegionalAdmin"]</c> to restrict who is an administrator used to yield
/// <c>["Administrator", "Admin", "RegionalAdmin"]</c>, so anyone holding a role named "Admin" stayed an
/// administrator against the operator's stated intent.
/// </para>
/// <para>
/// The alternative fix — emptying every default and re-applying it after binding — was not taken,
/// because it changes what an options object means when constructed directly with <c>new</c>, which
/// tests and application code both do today. Clearing only what the configuration actually specifies
/// leaves the defaults exactly where a reader of the type expects to find them.
/// </para>
/// </remarks>
public static class ConfigurationCollectionBinder
{
    /// <summary>
    /// Binds <paramref name="section"/> onto <paramref name="instance"/>, first clearing any writable
    /// collection property that the section explicitly specifies.
    /// </summary>
    /// <remarks>
    /// Only the instance's own top-level collections are handled. A collection nested inside another
    /// bound collection (an element the binder constructs itself) cannot be reached from here — those
    /// types apply their own defaults after binding instead. See
    /// <c>OAuthClientRegistrationOptions.NormalizeAndValidate</c> for that shape.
    /// </remarks>
    public static void BindReplacingSpecifiedCollections(IConfiguration section, object instance)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(instance);

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!IsClearableCollection(property))
            {
                continue;
            }

            // Exists() is the question that matters: "did the operator mention this key at all?".
            // Emptiness of the bound result cannot answer it — an operator who deliberately configures
            // an empty list means something different from one who says nothing.
            if (!section.GetSection(property.Name).Exists())
            {
                continue;
            }

            if (property.GetValue(instance) is IList { IsReadOnly: false } list)
            {
                list.Clear();
            }
        }

        section.Bind(instance);
    }

    /// <summary>
    /// A property the binder would append to: a readable, non-string list. Indexers are skipped because
    /// they cannot be read without arguments.
    /// </summary>
    private static bool IsClearableCollection(PropertyInfo property) =>
        property.CanRead
        && property.GetIndexParameters().Length == 0
        && property.PropertyType != typeof(string)
        && typeof(IList).IsAssignableFrom(property.PropertyType);
}
