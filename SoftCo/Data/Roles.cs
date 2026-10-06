namespace SoftCo.Data;

/// <summary>
/// The four roles this first slice needs. Deliberately fewer than the nine in the developer brief:
/// the tracker module only has to distinguish who may see money, who may change it, and who may
/// only look. The remaining roles arrive with the modules that need them.
/// </summary>
public static class Roles
{
    /// <summary>Full access, including user administration.</summary>
    public const string Admin = "Admin";

    /// <summary>Records payments, sees every value, exports.</summary>
    public const string Finance = "Finance";

    /// <summary>Creates and progresses orders; sees invoice values but cannot record payments.</summary>
    public const string Procurement = "Procurement";

    /// <summary>Read-only, and never sees payment or invoice values.</summary>
    public const string Viewer = "Viewer";

    /// <summary>
    /// Standard access for everyone onboarded through the Users screen: all day-to-day work -
    /// orders, payments, documents, payment requests - but no user administration.
    ///
    /// Administering users stays with <see cref="Admin"/> deliberately. Granting it to everyone
    /// would mean any account, once compromised, could mint more accounts; keeping it separate is
    /// the least privilege needed to do the job.
    /// </summary>
    public const string Staff = "Staff";

    public static readonly string[] All = [Admin, Finance, Procurement, Viewer, Staff];

    /// <summary>
    /// Every role that exists. Controllers require this rather than bare [Authorize] so that an
    /// account carrying no role at all is refused instead of admitted.
    ///
    /// Authentication alone used to be enough to reach the tracker, which meant a user whose role
    /// assignment had failed still saw every order, supplier and invoice value. Requiring a role
    /// makes the absence of one fail closed.
    /// </summary>
    public const string AnyRole = Admin + "," + Finance + "," + Procurement + "," + Viewer + "," + Staff;

    /// <summary>Roles permitted to see money columns anywhere in the UI.</summary>
    public const string CanSeeValues = Admin + "," + Finance + "," + Procurement + "," + Staff;

    /// <summary>Roles permitted to record or amend payments.</summary>
    public const string CanRecordPayments = Admin + "," + Finance + "," + Staff;

    /// <summary>Roles permitted to create or edit orders, suppliers and projects.</summary>
    public const string CanEditOrders = Admin + "," + Procurement + "," + Finance + "," + Staff;
}
