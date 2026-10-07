using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SoftCo.Models;

namespace SoftCo.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<SupplierOrder> SupplierOrders => Set<SupplierOrder>();
    public DbSet<OrderProject> OrderProjects => Set<OrderProject>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OrderDocument> OrderDocuments => Set<OrderDocument>();
    public DbSet<PaymentContact> PaymentContacts => Set<PaymentContact>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<CustomerInvoice> CustomerInvoices => Set<CustomerInvoice>();
    public DbSet<CustomerInvoiceLine> CustomerInvoiceLines => Set<CustomerInvoiceLine>();
    public DbSet<InvoiceReceipt> InvoiceReceipts => Set<InvoiceReceipt>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // --- Supplier ------------------------------------------------------------------------
        b.Entity<Supplier>(e =>
        {
            // Suppliers arrive from the tracker under long, inconsistent names
            // ("EMORY _ Foshan Shunde Xintimeinuo"), so the unique index is the guard against the
            // same manufacturer being entered twice under slightly different spellings.
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Type);
        });

        // --- Client --------------------------------------------------------------------------
        b.Entity<Client>(e =>
        {
            // One client entered twice under two spellings would split its invoices and quietly
            // halve the revenue side of its margin, so both are unique. Code is normalised to
            // capitals on save, so a plain index is enough for it.
            e.HasIndex(x => x.Code).IsUnique();

            // Name is NOT indexed here. A plain unique index is case-sensitive in PostgreSQL, so
            // it would happily accept "Williams" alongside "williams" - precisely the duplicate it
            // is meant to stop. The migration creates a unique index on lower("Name") instead,
            // which the controller's case-insensitive pre-check then matches rather than merely
            // approximates. Adding e.HasIndex(x => x.Name) back would create a second, weaker
            // index beside it.

            // The register lists active clients first and the "not synced" badge filters on this.
            e.HasIndex(x => x.SyncStatus);
        });

        // --- Project -------------------------------------------------------------------------
        b.Entity<Project>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();

            e.HasOne(x => x.Client)
             .WithMany(c => c.Projects)
             .HasForeignKey(x => x.ClientId)
             // Deleting a client must never take its project history with it, nor orphan the
             // invoices hanging off those projects. Same reasoning as SupplierOrder -> Supplier.
             .OnDelete(DeleteBehavior.Restrict);

            // Drives the "unassigned" count on the projects register.
            e.HasIndex(x => x.ClientId);
        });

        // --- Approval ------------------------------------------------------------------------
        b.Entity<Approval>(e =>
        {
            // Exactly one subject, enforced by the database rather than by trusting every caller.
            // CustomerInvoiceId has no foreign key yet - the table arrives in Phase 6 - but the
            // column and this constraint exist now, so adding it later does not mean revalidating
            // every row against a rewritten constraint.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Approvals_OneSubject",
                "num_nonnulls(\"SupplierOrderId\", \"PaymentRequestId\", \"CustomerInvoiceId\") = 1"));

            e.HasOne(x => x.SupplierOrder)
             .WithMany(o => o.Approvals)
             .HasForeignKey(x => x.SupplierOrderId)
             // An approval is evidence of a decision. Deleting what it refers to must fail loudly,
             // the same reasoning applied to SupplierOrder -> Supplier and PaymentRequest -> Contact.
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.PaymentRequest)
             .WithMany()
             .HasForeignKey(x => x.PaymentRequestId)
             .OnDelete(DeleteBehavior.Restrict);

            // The third subject. The column and the check constraint have been here since the
            // approvals table was created; this is the foreign key that was waiting for the
            // invoice table to exist.
            e.HasOne(x => x.CustomerInvoice)
             .WithMany(i => i.Approvals)
             .HasForeignKey(x => x.CustomerInvoiceId)
             .OnDelete(DeleteBehavior.Restrict);

            // The Financial Director's queue: open requests, newest first, by kind.
            e.HasIndex(x => new { x.Status, x.Kind, x.RequestedAt });

            // "What has this order been through?" on the detail page.
            e.HasIndex(x => x.SupplierOrderId);
        });

        // --- CustomerInvoice -----------------------------------------------------------------
        b.Entity<CustomerInvoice>(e =>
        {
            // Allocated on issue, so it must stay nullable while a draft is being worked on - the
            // same filtered unique index SupplierOrder.PoNumber uses.
            e.HasIndex(x => x.InvoiceNumber).IsUnique().HasFilter("\"InvoiceNumber\" IS NOT NULL");

            e.HasOne(x => x.Client)
             .WithMany()
             .HasForeignKey(x => x.ClientId)
             // An invoiced client is part of the financial record and cannot be deleted away from
             // the invoice that names them.
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Project)
             .WithMany()
             .HasForeignKey(x => x.ProjectId)
             .OnDelete(DeleteBehavior.Restrict);

            // The grid filters on status first, then narrows by client or project.
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.SyncStatus);

            // Issuing an invoice depends on knowing it has not moved underneath the person issuing
            // it, exactly as approving an order does.
            e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });

        // --- InvoiceReceipt ------------------------------------------------------------------
        b.Entity<InvoiceReceipt>(e =>
        {
            e.HasOne(x => x.CustomerInvoice)
             .WithMany(i => i.Receipts)
             .HasForeignKey(x => x.CustomerInvoiceId)
             // Restrict, unlike invoice lines. A line has no meaning apart from its invoice, but a
             // receipt is money that actually arrived, and it must not be able to vanish because
             // somebody deleted the document it was received against.
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.CustomerInvoiceId);
            e.HasIndex(x => x.ReceivedDate);
            e.HasIndex(x => x.SyncStatus);
        });

        // --- CreditNote ----------------------------------------------------------------------
        b.Entity<CreditNote>(e =>
        {
            // Allocated on issue, so nullable while drafting - the same filtered unique index the
            // PO and invoice numbers use.
            e.HasIndex(x => x.CreditNoteNumber).IsUnique().HasFilter("\"CreditNoteNumber\" IS NOT NULL");

            e.HasOne(x => x.CustomerInvoice)
             .WithMany(i => i.CreditNotes)
             .HasForeignKey(x => x.CustomerInvoiceId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.CustomerInvoiceId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SyncStatus);
        });

        // --- CreditNoteLine --------------------------------------------------------------------
        b.Entity<CreditNoteLine>(e =>
        {
            e.HasOne(x => x.CreditNote)
             .WithMany(c => c.Lines)
             .HasForeignKey(x => x.CreditNoteId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CreditNoteId);
        });

        // --- CustomerInvoiceLine -------------------------------------------------------------
        b.Entity<CustomerInvoiceLine>(e =>
        {
            e.HasOne(x => x.CustomerInvoice)
             .WithMany(i => i.Lines)
             .HasForeignKey(x => x.CustomerInvoiceId)
             // Cascade, unlike everything else here: a line has no meaning apart from its invoice,
             // and only a draft can be deleted at all.
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CustomerInvoiceId);
        });

        // --- SupplierOrder -------------------------------------------------------------------
        b.Entity<SupplierOrder>(e =>
        {
            e.HasOne(x => x.Supplier)
             .WithMany(s => s.Orders)
             .HasForeignKey(x => x.SupplierId)
             // Deleting a supplier must never silently take its financial history with it.
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.FulfilmentStatus);
            e.HasIndex(x => x.InvoiceRef);
            e.HasIndex(x => x.InvoiceDate);

            // The internal reference must be unique for the life of the system. Filtered so the
            // column can stay nullable while a row is being created.
            e.HasIndex(x => x.PoNumber).IsUnique().HasFilter("\"PoNumber\" IS NOT NULL");

            // Both grids filter on this first, so it leads every query.
            e.HasIndex(x => x.OrderType);

            // The approval queue and the order grid both narrow on this.
            e.HasIndex(x => x.PoApprovalStatus);

            // Optimistic concurrency via PostgreSQL's xmin system column. No column is added -
            // xmin already exists on every row - so this costs nothing in schema terms. It matters
            // because approvals are about to record "the FD approved THIS version of the order":
            // two people editing the same order would otherwise last-write-win in silence.
            e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });

        // --- OrderProject (join) -------------------------------------------------------------
        b.Entity<OrderProject>(e =>
        {
            e.HasKey(x => new { x.SupplierOrderId, x.ProjectId });

            // A share outside 0..1 is nonsense, and the database says so. That an order's shares sum
            // to 1 is a statement about a set of rows rather than about one, so it lives in the
            // service - but this half can be enforced here and is.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_OrderProjects_AllocationShare",
                "\"AllocationShare\" >= 0 AND \"AllocationShare\" <= 1"));

            e.HasOne(x => x.SupplierOrder)
             .WithMany(o => o.OrderProjects)
             .HasForeignKey(x => x.SupplierOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Project)
             .WithMany(p => p.OrderProjects)
             .HasForeignKey(x => x.ProjectId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // --- OrderPayment --------------------------------------------------------------------
        b.Entity<OrderPayment>(e =>
        {
            e.HasOne(x => x.SupplierOrder)
             .WithMany(o => o.Payments)
             .HasForeignKey(x => x.SupplierOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PaidDate);
        });

        // --- OrderDocument -------------------------------------------------------------------
        b.Entity<OrderDocument>(e =>
        {
            e.HasOne(x => x.SupplierOrder)
             .WithMany(o => o.Documents)
             .HasForeignKey(x => x.SupplierOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.SupplierOrderId, x.Kind });
        });

        // --- PaymentContact ------------------------------------------------------------------
        b.Entity<PaymentContact>(e =>
        {
            // One address, one contact - stops the same person being added three times with
            // three spellings of their name.
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.IsActive);
        });

        // --- PaymentRequest ------------------------------------------------------------------
        b.Entity<PaymentRequest>(e =>
        {
            e.HasOne(x => x.SupplierOrder)
             .WithMany(o => o.PaymentRequests)
             .HasForeignKey(x => x.SupplierOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            // Restrict, not cascade: deleting a contact must never erase the evidence that a
            // request was sent to them.
            e.HasOne(x => x.PaymentContact)
             .WithMany()
             .HasForeignKey(x => x.PaymentContactId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.SentAt);

            // Same reasoning as SupplierOrder: a payment request about to carry an approval state
            // must not move underneath the person approving it.
            e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });

        // --- AuditEvent ----------------------------------------------------------------------
        b.Entity<AuditEvent>(e =>
        {
            e.HasIndex(x => new { x.EntityName, x.EntityId });
            e.HasIndex(x => x.OccurredAt);
        });
    }
}
