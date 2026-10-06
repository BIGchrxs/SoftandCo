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
