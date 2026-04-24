using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing the database.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the AppDbContext class.
    /// </summary>
    /// <param name="options"></param>
    public AppDbContext(DbContextOptions<AppDbContext> options) :
        base(options)
    {
    }

    public DbSet<User> Users => this.Set<User>();
    public DbSet<AllowlistEntry> Allowlist => this.Set<AllowlistEntry>();
    public DbSet<Property> Properties => this.Set<Property>();
    public DbSet<Reservation> Reservations => this.Set<Reservation>();
    public DbSet<BlackoutDate> BlackoutDates => this.Set<BlackoutDate>();
    public DbSet<FavoriteSpot> FavoriteSpots => this.Set<FavoriteSpot>();
    public DbSet<WalkingPath> WalkingPaths => this.Set<WalkingPath>();
    public DbSet<GuestDoorCode> GuestDoorCodes => this.Set<GuestDoorCode>();
    public DbSet<Setting> Settings => this.Set<Setting>();
    public DbSet<Session> Sessions => this.Set<Session>();
    public DbSet<GuestbookEntry> GuestbookEntries => this.Set<GuestbookEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<string>();
        configurationBuilder.Properties<Guid?>().HaveConversion<string?>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateOnlyConverter = new ValueConverter<DateOnly, string>(
            d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            s => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        var dateTimeConverter = new ValueConverter<DateTime, string>(
            d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            s => DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        var nullableTimeOnlyConverter = new ValueConverter<TimeOnly?, string?>(
            t => t == null ? null : t.Value.ToString("HH\\:mm", CultureInfo.InvariantCulture),
            s => s == null ? null : TimeOnly.ParseExact(s, "HH\\:mm", CultureInfo.InvariantCulture));

        var nullableDateTimeConverter = new ValueConverter<DateTime?, string?>(
            d => d == null ? null : d.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            s => s == null ? null : DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Email).HasColumnName("email");
            e.Property(u => u.Name).HasColumnName("name");
            e.Property(u => u.Picture).HasColumnName("picture");
            e.Property(u => u.IsAdmin).HasColumnName("is_admin").HasConversion<int>();
            e.Property(u => u.AuthProvider).HasColumnName("auth_provider");
            e.Property(u => u.CalendarToken).HasColumnName("calendar_token");
            e.Property(u => u.SecurityVersion).HasColumnName("security_version");
            e.Property(u => u.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasConversion(dateTimeConverter);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<AllowlistEntry>(e =>
        {
            e.ToTable("allowlist");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Email).HasColumnName("email");
            e.Property(a => a.AddedBy).HasColumnName("added_by");
            e.Property(a => a.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.HasIndex(a => a.Email).IsUnique();
            e.HasOne(a => a.AddedByUser).WithMany(u => u.AllowlistEntries).HasForeignKey(a => a.AddedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Property>(e =>
        {
            e.ToTable("properties");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.Address).HasColumnName("address");
            e.Property(p => p.WifiNetwork).HasColumnName("wifi_network");
            e.Property(p => p.WifiPassword).HasColumnName("wifi_password");
            e.Property(p => p.DoorCode).HasColumnName("door_code");
            e.Property(p => p.IcalToken).HasColumnName("ical_token");
            e.Property(p => p.Information).HasColumnName("information");
            e.Property(p => p.CheckinProcedures).HasColumnName("checkin_procedures");
            e.Property(p => p.CheckoutProcedures).HasColumnName("checkout_procedures");
            e.Property(p => p.TimeZone).HasColumnName("time_zone");
            e.Property(p => p.IsActive).HasColumnName("is_active").HasConversion<int>();
            e.Property(p => p.MaxConsecutiveNights).HasColumnName("max_consecutive_nights");
            e.Property(p => p.MinAdvanceNoticeDays).HasColumnName("min_advance_notice_days");
            e.Property(p => p.MaxAdvanceBookingDays).HasColumnName("max_advance_booking_days");
            e.Property(p => p.AccessWindowDays).HasColumnName("access_window_days");
            e.Property(p => p.CheckInTime).HasColumnName("check_in_time").HasConversion(nullableTimeOnlyConverter);
            e.Property(p => p.CheckOutTime).HasColumnName("check_out_time").HasConversion(nullableTimeOnlyConverter);
            e.Property(p => p.Latitude).HasColumnName("latitude");
            e.Property(p => p.Longitude).HasColumnName("longitude");
            e.Property(p => p.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasConversion(dateTimeConverter);
        });

        modelBuilder.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.UserId).HasColumnName("user_id");
            e.Property(r => r.PropertyId).HasColumnName("property_id");
            e.Property(r => r.CheckIn).HasColumnName("check_in").HasConversion(dateOnlyConverter);
            e.Property(r => r.CheckOut).HasColumnName("check_out").HasConversion(dateOnlyConverter);
            e.Property(r => r.Notes).HasColumnName("notes");
            e.Property(r => r.DeletedAt).HasColumnName("deleted_at").HasConversion(nullableDateTimeConverter);
            e.Property(r => r.ModifiedAt).HasColumnName("modified_at").HasConversion(nullableDateTimeConverter);
            e.Property(r => r.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.HasOne(r => r.User).WithMany(u => u.Reservations).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Property).WithMany(p => p.Reservations).HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BlackoutDate>(e =>
        {
            e.ToTable("blackout_dates");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasColumnName("id");
            e.Property(b => b.PropertyId).HasColumnName("property_id");
            e.Property(b => b.Date).HasColumnName("date");
            e.Property(b => b.Reason).HasColumnName("reason");
            e.Property(b => b.CreatedBy).HasColumnName("created_by");
            e.Property(b => b.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.HasOne(b => b.Property).WithMany(p => p.BlackoutDates).HasForeignKey(b => b.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.CreatedByUser).WithMany().HasForeignKey(b => b.CreatedBy).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FavoriteSpot>(e =>
        {
            e.ToTable("favorite_spots");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.PropertyId).HasColumnName("property_id");
            e.Property(f => f.Name).HasColumnName("name");
            e.Property(f => f.Address).HasColumnName("address");
            e.Property(f => f.Description).HasColumnName("description");
            e.Property(f => f.Latitude).HasColumnName("latitude");
            e.Property(f => f.Longitude).HasColumnName("longitude");
            e.Property(f => f.SortOrder).HasColumnName("sort_order");
            e.Property(f => f.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.HasOne(f => f.Property).WithMany(p => p.FavoriteSpots).HasForeignKey(f => f.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WalkingPath>(e =>
        {
            e.ToTable("walking_paths");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasColumnName("id");
            e.Property(w => w.PropertyId).HasColumnName("property_id");
            e.Property(w => w.Name).HasColumnName("name");
            e.Property(w => w.Color).HasColumnName("color");
            e.Property(w => w.Weight).HasColumnName("weight");
            e.Property(w => w.Opacity).HasColumnName("opacity");
            e.Property(w => w.Coordinates).HasColumnName("coordinates");
            e.Property(w => w.SortOrder).HasColumnName("sort_order");
            e.Property(w => w.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.HasOne(w => w.Property).WithMany(p => p.WalkingPaths).HasForeignKey(w => w.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuestDoorCode>(e =>
        {
            e.ToTable("guest_door_codes");
            e.HasKey(g => new { g.UserId, g.PropertyId });
            e.Property(g => g.UserId).HasColumnName("user_id");
            e.Property(g => g.PropertyId).HasColumnName("property_id");
            e.Property(g => g.DoorCode).HasColumnName("door_code");
            e.HasOne(g => g.User).WithMany(u => u.GuestDoorCodes).HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(g => g.Property).WithMany(p => p.GuestDoorCodes).HasForeignKey(g => g.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuestbookEntry>(e =>
        {
            e.ToTable("guestbook_entries");
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).HasColumnName("id");
            e.Property(g => g.ReservationId).HasColumnName("reservation_id");
            e.Property(g => g.PropertyId).HasColumnName("property_id");
            e.Property(g => g.UserId).HasColumnName("user_id");
            e.Property(g => g.Message).HasColumnName("message");
            e.Property(g => g.CreatedAt).HasColumnName("created_at").HasConversion(dateTimeConverter);
            e.Property(g => g.UpdatedAt).HasColumnName("updated_at").HasConversion(nullableDateTimeConverter);
            e.HasOne(g => g.Reservation).WithMany(r => r.GuestbookEntries).HasForeignKey(g => g.ReservationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(g => g.Property).WithMany(p => p.GuestbookEntries).HasForeignKey(g => g.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(g => g.User).WithMany(u => u.GuestbookEntries).HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(g => g.ReservationId).IsUnique();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasColumnName("key");
            e.Property(s => s.Value).HasColumnName("value");
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(s => s.Token);
            e.Property(s => s.Token).HasColumnName("token");
            e.Property(s => s.Data).HasColumnName("data");
            e.Property(s => s.Expiry).HasColumnName("expiry");
            e.Property(s => s.UserId).HasColumnName("user_id");
            e.Property(s => s.SecurityVersion).HasColumnName("security_version");
            e.Property(s => s.IssuedAt).HasColumnName("issued_at");
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
