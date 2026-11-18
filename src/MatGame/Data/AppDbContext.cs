using Microsoft.EntityFrameworkCore;

namespace MatGame.Data;

/// <summary>
/// The database of MatGame (PostgreSQL, schema <c>matgame</c>; every game keeps its own schema). Tables and columns are PascalCase, every key is a bigint "Id".
/// Saving fills the audit columns: CreateDate/CreateUserId for new records, UpdateDate/UpdateUserId always, UpdateState = Updated for changed soft-delete records.
/// The business layer says who acts with <see cref="ActingAs"/>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string Schema = "matgame";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceLink> DeviceLinks => Set<DeviceLink>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<GameSetting> GameSettings => Set<GameSetting>();

    /// <summary>The account whose changes are saved (null = the system).</summary>
    public long? ActorId { get; private set; }

    public AppDbContext ActingAs(long? accountId)
    {
        ActorId = accountId;
        return this;
    }

    /// <summary>The options of the context on a connection string (the migrations history lives in the schema too).</summary>
    public static void Use(DbContextOptionsBuilder options, string connection)
        => options.UseNpgsql(connection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("Account");
            e.Property(a => a.LoginName).HasMaxLength(60);
            e.Property(a => a.DisplayName).HasMaxLength(60);
            e.Property(a => a.PasswordHash).HasMaxLength(200);
            e.Property(a => a.IdentitySubject).HasMaxLength(200);
            e.HasIndex(a => a.LoginName).IsUnique();
            e.HasIndex(a => a.IdentitySubject).IsUnique().HasFilter("\"IdentitySubject\" IS NOT NULL");
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable("Device");
            e.Property(d => d.Name).HasMaxLength(80);
            e.Property(d => d.Platform).HasMaxLength(20);
            e.Property(d => d.Game).HasMaxLength(40);
            e.Property(d => d.TokenHash).HasMaxLength(64);
            e.HasIndex(d => d.TokenHash).IsUnique();
            e.HasOne(d => d.Account).WithMany().HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceLink>(e =>
        {
            e.ToTable("DeviceLink");
            e.Property(l => l.Code).HasMaxLength(9);
            e.Property(l => l.DeviceCodeHash).HasMaxLength(64);
            e.Property(l => l.DeviceName).HasMaxLength(80);
            e.Property(l => l.Platform).HasMaxLength(20);
            e.Property(l => l.Game).HasMaxLength(40);
            e.HasIndex(l => l.Code);
            e.HasIndex(l => l.DeviceCodeHash).IsUnique();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("Setting");
            e.Property(s => s.Key).HasMaxLength(60);
            e.Property(s => s.Value).HasMaxLength(2000);
            e.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<GameSetting>(e =>
        {
            e.ToTable("GameSetting");
            e.Property(g => g.GameId).HasMaxLength(40);
            e.Property(g => g.MinClient).HasMaxLength(40);
            e.HasIndex(g => g.GameId).IsUnique();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateDate = now;
                entry.Entity.CreateUserId = ActorId;
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId = ActorId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId = ActorId;
                if (entry.Entity is SoftDeleteEntity soft && soft.UpdateState != UpdateState.Deleted)
                {
                    soft.UpdateState = UpdateState.Updated;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>For <c>dotnet ef</c>: the context on the local stack's database.</summary>
public sealed class DesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Use(options, "Host=localhost;Port=2775;Database=matgame;Username=matgame;Password=matgame");
        return new AppDbContext(options.Options);
    }
}
