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

    /// <summary>
    /// Approves purchase orders, payment releases and client invoices before Soft &amp; Co is
    /// committed to anything.
    ///
    /// Deliberately not a superset of <see cref="Finance"/>. The Financial Director approves; they
    /// do not raise orders or record payments. Keeping the two apart is what makes "the approver is
    /// not the submitter" enforceable rather than a convention - an account that could do both
    /// would be able to approve its own work.
    /// </summary>
    public const string FinancialDirector = "FinancialDirector";

    public static readonly string[] All = [Admin, Finance, Procurement, Viewer, Staff, FinancialDirector];

    /// <summary>
    /// The roles the Users screen may hand out. A deliberate subset of <see cref="All"/>: Admin is
    /// not here, so nobody can mint another administrator through the ordinary onboarding form, and
    /// the legacy Finance/Procurement/Viewer roles are kept out of new accounts rather than being
    /// offered as choices nobody can explain.
    ///
    /// Any role posted from a form is checked against this list, so an arbitrary string - including
    /// "Admin" - cannot be assigned by editing the request.
    /// </summary>
    public static readonly string[] Assignable = [Staff, FinancialDirector];

    /// <summary>How a role reads on screen.</summary>
    public static string Label(string role) => role switch
    {
        Admin => "Administrator",
        FinancialDirector => "Financial Director",
        Staff => "Staff",
        Finance => "Finance",
        Procurement => "Procurement",
        Viewer => "Viewer",
        _ => role
    };

    /// <summary>
    /// Every role that exists. Controllers require this rather than bare [Authorize] so that an
    /// account carrying no role at all is refused instead of admitted.
    ///
    /// Authentication alone used to be enough to reach the tracker, which meant a user whose role
    /// assignment had failed still saw every order, supplier and invoice value. Requiring a role
    /// makes the absence of one fail closed.
    /// </summary>
    public const string AnyRole = Admin + "," + Finance + "," + Procurement + "," + Viewer + "," + Staff
                                 + "," + FinancialDirector;

    /// <summary>Roles permitted to see money columns anywhere in the UI.</summary>
    public const string CanSeeValues = Admin + "," + Finance + "," + Procurement + "," + Staff
                                       + "," + FinancialDirector;

    /// <summary>Roles permitted to record or amend payments.</summary>
    public const string CanRecordPayments = Admin + "," + Finance + "," + Staff;

    /// <summary>Roles permitted to create or edit orders, suppliers, projects and clients.</summary>
    public const string CanEditOrders = Admin + "," + Procurement + "," + Finance + "," + Staff;

    /// <summary>
    /// Roles permitted to approve or reject what has been submitted. Admin is included because
    /// somebody must be able to act when the Financial Director is away; every decision records who
    /// made it, so that is visible rather than silent.
    /// </summary>
    public const string CanApprove = Admin + "," + FinancialDirector;

    /// <summary>
    /// Roles permitted to see margin and gross profit. Narrower than <see cref="CanSeeValues"/> on
    /// purpose: what an order cost and what a client was charged are both visible to the people
    /// doing the work, but the difference between them is not.
    /// </summary>
    public const string CanSeeMargin = Admin + "," + Finance + "," + FinancialDirector;
}
