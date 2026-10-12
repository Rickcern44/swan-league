using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Swan.Core.Communication;
using Swan.Core.Common;
using Swan.Core.Finance;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Core.Picks;
using Swan.Core.Platform;
using Swan.Core.Schedule;
using Swan.Core.Scoring;

namespace Swan.Data;

public sealed class SwanDbContext(DbContextOptions<SwanDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<SeasonTeam> SeasonTeams => Set<SeasonTeam>();
    public DbSet<Week> Weeks => Set<Week>();
    public DbSet<Roster> Rosters => Set<Roster>();
    public DbSet<Ballot> Ballots => Set<Ballot>();
    public DbSet<WeekScore> WeekScores => Set<WeekScore>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<PoolSettlement> PoolSettlements => Set<PoolSettlement>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureIdentity(modelBuilder);
        ConfigureLeague(modelBuilder);
        ConfigureSchedule(modelBuilder);
        ConfigurePicks(modelBuilder);
        ConfigureScoring(modelBuilder);
        ConfigureFinance(modelBuilder);
        ConfigureCommunication(modelBuilder);
        ConfigurePlatform(modelBuilder);
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", "identity");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(user => user.Email).HasConversion(email => email.Value, value => new Email(value)).HasMaxLength(320);
            entity.Property(user => user.Name).HasMaxLength(200);
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Ignore(user => user.DomainEvents);
            entity.Ignore(user => user.LoginTokens);
        });
    }

    private static void ConfigureLeague(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Season>(entity =>
        {
            entity.ToTable("seasons", "league");
            entity.HasKey(season => season.Id);
            entity.Property(season => season.Id).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Ignore(season => season.DomainEvents);
            entity.Property(season => season.Settings).HasConversion(value => $"{value.BuyIn.Amount}|{value.BuyIn.Currency}|{value.DoubleDogWeeks}", value => ParseSeasonSettings(value));
        });
        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships", "league");
            entity.HasKey(membership => membership.Id);
            entity.Property(membership => membership.Id).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Property(membership => membership.UserId).HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(membership => membership.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Property(membership => membership.Role);
            entity.HasIndex(membership => new { membership.SeasonId, membership.UserId }).IsUnique();
            entity.Ignore(membership => membership.DomainEvents);
        });
    }

    private static void ConfigureSchedule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams", "schedule");
            entity.HasKey(team => team.Id);
            entity.Property(team => team.Id).HasConversion(id => id.Value, value => new TeamId(value));
            entity.Property(team => team.Name);
            entity.Property(team => team.LogoUrl);
            entity.Property(team => team.CreatedAt);
        });
        modelBuilder.Entity<SeasonTeam>(entity =>
        {
            entity.ToTable("season_teams", "schedule");
            entity.HasKey(team => team.Id);
            entity.Property(team => team.Id).HasConversion(id => id.Value, value => new SeasonTeamId(value));
            entity.Property(team => team.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Property(team => team.TeamId).HasConversion(id => id.Value, value => new TeamId(value));
            entity.Property(team => team.Division);
            entity.Property(team => team.FinishPosition);
            entity.Property(team => team.FinishTies);
        });
        modelBuilder.Entity<Week>(entity =>
        {
            entity.ToTable("weeks", "schedule");
            entity.HasKey(week => week.Id);
            entity.Property(week => week.Id).HasConversion(id => id.Value, value => new WeekId(value));
            entity.Property(week => week.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Property(week => week.Number);
            entity.Property(week => week.State);
            entity.Ignore(week => week.DomainEvents);
            entity.Property(week => week.Type).HasConversion(value => $"{(int)value.Kind}|{value.Round}|{value.PicksRequired}", value => ParseWeekType(value));
            entity.OwnsMany<Game>(nameof(Game), nameof(Week.Games), game =>
            {
                game.ToTable("games", "schedule");
                game.WithOwner().HasForeignKey("WeekId");
                game.HasKey(value => value.Id);
                game.Property(value => value.Id).HasConversion(id => id.Value, value => new GameId(value));
                game.Property(value => value.HomeTeamId).HasConversion(id => id.Value, value => new TeamId(value));
                game.Property(value => value.AwayTeamId).HasConversion(id => id.Value, value => new TeamId(value));
                game.Property(value => value.Kickoff).HasConversion(value => value.Value, value => new Kickoff(value));
                game.Property(value => value.Excluded);
                game.Property(value => value.ApprovedLine).HasConversion(new ValueConverter<Spread?, decimal?>(value => value == null ? null : value.Value, value => value == null ? null : new Spread(value.Value)));
                game.Property(value => value.Result).HasConversion(new ValueConverter<GameResult?, string?>(value => value == null ? null : $"{value.HomeScore}:{value.AwayScore}", value => value == null ? null : ParseResult(value)));
                game.Ignore(value => value.LineHistory);
            });
            entity.OwnsMany<DogOption>(nameof(DogOption), nameof(Week.DogOptions), option =>
            {
                option.ToTable("dog_options", "schedule");
                option.WithOwner().HasForeignKey("WeekId");
                option.Property<Guid>("Id");
                option.HasKey("Id");
                option.Property(value => value.TeamId).HasConversion(id => id.Value, value => new TeamId(value));
            });
        });
    }

    private static void ConfigurePicks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Roster>(entity =>
        {
            entity.ToTable("rosters", "picks");
            entity.HasKey(roster => roster.Id);
            entity.Property(roster => roster.Id).HasConversion(id => id.Value, value => new RosterId(value));
            entity.Property(roster => roster.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Property(roster => roster.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Ignore(roster => roster.DomainEvents);
            entity.Ignore(roster => roster.Teams);
        });
        modelBuilder.Entity<Ballot>(entity =>
        {
            entity.ToTable("ballots", "picks");
            entity.HasKey(ballot => ballot.Id);
            entity.Property(ballot => ballot.Id).HasConversion(id => id.Value, value => new BallotId(value));
            entity.Property(ballot => ballot.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Property(ballot => ballot.WeekId).HasConversion(id => id.Value, value => new WeekId(value));
            entity.Ignore(ballot => ballot.DomainEvents);
            entity.OwnsMany<TeamPick>(nameof(TeamPick), nameof(Ballot.Picks), pick =>
            {
                pick.ToTable("team_picks", "picks");
                pick.WithOwner().HasForeignKey("BallotId");
                pick.Property<Guid>("Id");
                pick.HasKey("Id");
                pick.Property(value => value.TeamId).HasConversion(id => id.Value, value => new TeamId(value));
                pick.Property(value => value.GameId).HasConversion(id => id.Value, value => new GameId(value));
                pick.Property(value => value.LockTime);
                pick.Property(value => value.IsBye);
            });
            entity.OwnsMany<DogPick>(nameof(DogPick), nameof(Ballot.Dogs), dog =>
            {
                dog.ToTable("dog_picks", "picks");
                dog.WithOwner().HasForeignKey("BallotId");
                dog.Property<Guid>("Id");
                dog.HasKey("Id");
                dog.Property(value => value.TeamId).HasConversion(id => id.Value, value => new TeamId(value));
                dog.Property(value => value.GameId).HasConversion(id => id.Value, value => new GameId(value));
                dog.Property(value => value.LockTime);
            });
            entity.OwnsMany<PickChange>(nameof(PickChange), nameof(Ballot.Changes), change =>
            {
                change.ToTable("pick_changes", "picks");
                change.WithOwner().HasForeignKey("BallotId");
                change.Property<Guid>("Id");
                change.HasKey("Id");
                change.Property(value => value.ChangedAt);
                change.Property(value => value.Reason);
            });
        });
    }

    private static void ConfigureScoring(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeekScore>(entity =>
        {
            entity.ToTable("week_scores", "scoring");
            entity.HasKey(score => score.Id);
            entity.Property(score => score.Id).HasConversion(id => id.Value, value => new WeekScoreId(value));
            entity.Property(score => score.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Property(score => score.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Property(score => score.WeekId).HasConversion(id => id.Value, value => new WeekId(value));
            entity.Ignore(score => score.DomainEvents);
            entity.Ignore(score => score.Lines);
        });
    }

    private static void ConfigureFinance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>(entity =>
        {
            entity.ToTable("ledgers", "money");
            entity.HasKey(ledger => ledger.Id);
            entity.Property(ledger => ledger.Id).HasConversion(id => id.Value, value => new LedgerId(value));
            entity.Property(ledger => ledger.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Ignore(ledger => ledger.DomainEvents);
            entity.OwnsMany<LedgerItem>(nameof(LedgerItem), nameof(Ledger.Items), item =>
            {
                item.ToTable("ledger_items", "money");
                item.WithOwner().HasForeignKey("LedgerId");
                item.HasKey(value => value.Id);
                item.Property(value => value.Id);
                item.Property(value => value.Memo);
                item.Property(value => value.PayerId).HasConversion(id => id.Value, value => new MembershipId(value));
                item.Property(value => value.PayeeId).HasConversion(id => id.Value, value => new MembershipId(value));
                item.Property(value => value.AdminId).HasConversion(id => id.Value, value => new MembershipId(value));
                item.Property(value => value.Status);
                item.Property(value => value.Amount).HasConversion(value => $"{value.Amount}|{value.Currency}", value => ParseMoney(value));
            });
        });
        modelBuilder.Entity<PoolSettlement>(entity =>
        {
            entity.ToTable("pool_settlements", "money");
            entity.HasKey(settlement => settlement.Id);
            entity.Property(settlement => settlement.Id).HasConversion(id => id.Value, value => new PoolSettlementId(value));
            entity.Property(settlement => settlement.SeasonId).HasConversion(id => id.Value, value => new SeasonId(value));
            entity.Ignore(settlement => settlement.DomainEvents);
            entity.Property(settlement => settlement.Pot).HasConversion(value => $"{value.Amount}|{value.Currency}", value => ParseMoney(value));
            entity.OwnsMany<Placing>(nameof(Placing), nameof(PoolSettlement.Placements), placing =>
            {
                placing.ToTable("pool_placements", "money");
                placing.WithOwner().HasForeignKey("PoolSettlementId");
                placing.Property<Guid>("Id");
                placing.HasKey("Id");
                placing.Property(value => value.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
                placing.Property(value => value.Position);
                placing.Property(value => value.Payout).HasConversion(value => $"{value.Amount}|{value.Currency}", value => ParseMoney(value));
            });
        });
    }

    private static void ConfigureCommunication(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable("announcements", "communication");
            entity.HasKey(announcement => announcement.Id);
            entity.Property(announcement => announcement.Id).HasConversion(id => id.Value, value => new AnnouncementId(value));
            entity.Property(announcement => announcement.Content);
            entity.Property(announcement => announcement.ScheduledFor);
            entity.Property(announcement => announcement.IsSent);
        });
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications", "communication");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Id).HasConversion(id => id.Value, value => new NotificationId(value));
            entity.Property(notification => notification.RecipientId).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Property(notification => notification.EventKey);
            entity.Property(notification => notification.Content);
            entity.Property(notification => notification.IsRead);
            entity.HasIndex(notification => new { notification.RecipientId, notification.EventKey }).IsUnique();
        });
        modelBuilder.Entity<NotificationPreferences>(entity =>
        {
            entity.ToTable("notification_preferences", "communication");
            entity.HasKey(preferences => preferences.MembershipId);
            entity.Property(preferences => preferences.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
        });
        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions", "communication");
            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.Id).HasConversion(id => id.Value, value => new PushSubscriptionId(value));
            entity.Property(subscription => subscription.MembershipId).HasConversion(id => id.Value, value => new MembershipId(value));
            entity.Property(subscription => subscription.Endpoint);
            entity.Property(subscription => subscription.P256Dh);
            entity.Property(subscription => subscription.Auth);
            entity.HasIndex(subscription => subscription.Endpoint).IsUnique();
        });
    }

    private static void ConfigurePlatform(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entries", "platform");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasConversion(id => id.Value, value => new AuditEntryId(value));
            entity.Property(entry => entry.UserId).HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(entry => entry.Action);
            entity.Property(entry => entry.At);
            entity.Property(entry => entry.ResourceId);
            entity.Property(entry => entry.Details);
        });
        modelBuilder.Entity<ScheduledJob>(entity =>
        {
            entity.ToTable("scheduled_jobs", "platform");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasConversion(id => id.Value, value => new ScheduledJobId(value));
            entity.Property(job => job.Key);
            entity.Property(job => job.NextRunAt);
            entity.Property(job => job.LeaseNodeId);
            entity.Property(job => job.LeaseExpiresAt);
            entity.HasIndex(job => job.Key).IsUnique();
        });
    }

    private static Money ParseMoney(string value)
    {
        var parts = value.Split('|');
        return new Money(decimal.Parse(parts[0]), parts[1]);
    }

    private static SeasonSettings ParseSeasonSettings(string value)
    {
        var parts = value.Split('|');
        return new SeasonSettings(new Money(decimal.Parse(parts[0]), parts[1]), int.Parse(parts[2]));
    }

    private static WeekType ParseWeekType(string value)
    {
        var parts = value.Split('|');
        return new WeekType((WeekKind)int.Parse(parts[0]), parts[1], bool.Parse(parts[2]));
    }

    private static GameResult ParseResult(string value)
    {
        var scores = value.Split(':');
        return new GameResult(int.Parse(scores[0]), int.Parse(scores[1]));
    }
}
