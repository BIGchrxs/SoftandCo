using System.ComponentModel.DataAnnotations;

namespace SoftCo.ViewModels;

// The project register's edit form.
// Namespace deliberately matches the other view-model files.

/// <summary>
/// Exists so that naming a client can be required on a new project without being required in the
/// database. The projects already in the register predate the client table - several of them are
/// internal and will never have a client - so <c>[Required]</c> on the entity, or a non-null
/// column, would make existing rows unsaveable.
/// </summary>
public class ProjectEditViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required, StringLength(50), Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200), Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Client")]
    public int? ClientId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Filled by the controller for the dropdown. Never posted back.</summary>
    public List<ClientOption> Clients { get; set; } = [];

    public bool IsNew => Id == 0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // New projects only, deliberately. An existing project with no client still saves, so
        // nobody is blocked from fixing a typo on a 2025 project by a field that did not exist
        // when it was created - the register's "unassigned" count is what drives those to be
        // filled in, rather than a wall in front of unrelated work.
        if (IsNew && ClientId is null)
            yield return new ValidationResult(
                "Choose the client this project is for.", [nameof(ClientId)]);
    }
}

/// <summary>One entry in the client dropdown. Flat and read-only, so no entity reaches the view.</summary>
public record ClientOption(int Id, string Code, string Name);
