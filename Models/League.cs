using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace CampeonatoMagic.Models;

public class Player
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DisplayName => IsDeleted ? $"{Name} (eliminado)" : Name;
}

public class Tournament
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(1000)] public string Description { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Closed { get; set; }
    public int? ChampionId { get; set; }
    public Player? Champion { get; set; }
    public List<Enrollment> Enrollments { get; set; } = [];
    public List<Game> Games { get; set; } = [];
    public List<Adjustment> Adjustments { get; set; } = [];
}

public class Enrollment
{
    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}

public enum Outcome { Eliminated, Winner, Survived, Conceded }

public class Game
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public DateTime PlayedAt { get; set; }
    [StringLength(1000)] public string Notes { get; set; } = "";
    public List<Seat> Seats { get; set; } = [];
}

public class Seat
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public Outcome Outcome { get; set; }
    public int? KillerId { get; set; }
    public Player? Killer { get; set; }
}

public class Adjustment
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int Points { get; set; }
    [Required, StringLength(250)] public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LeagueDb(DbContextOptions<LeagueDb> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Enrollment>().HasKey(x => new { x.TournamentId, x.PlayerId });
        b.Entity<Player>().HasIndex(x => x.Name).IsUnique().HasFilter("\"IsDeleted\" = 0");
        b.Entity<Seat>().HasIndex(x => new { x.GameId, x.PlayerId }).IsUnique();
        b.Entity<Seat>().HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Seat>().HasOne(x => x.Killer).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Tournament>().HasOne(x => x.Champion).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Adjustment>().HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}

public record Standing(int PlayerId, string Name, int Games, int Wins, int Kills, int Survival, int Adjustments, int Points);

public static class Scoring
{
    public const int WinPoints = 3;
    public const int KillPoints = 1;
    public const int SurvivalPoints = 1;
    public static int Points(Game game, Seat seat) =>
        (seat.Outcome == Outcome.Winner ? WinPoints : 0)
        + game.Seats.Count(x => x.KillerId == seat.PlayerId) * KillPoints
        + (seat.Outcome == Outcome.Survived && game.Seats.All(x => x.Outcome != Outcome.Winner) ? SurvivalPoints : 0);

    public static List<Standing> Table(Tournament t) => t.Enrollments.Select(e =>
    {
        var games = t.Games.Where(g => g.Seats.Any(s => s.PlayerId == e.PlayerId)).ToList();
        var seats = games.Select(g => g.Seats.Single(s => s.PlayerId == e.PlayerId)).ToList();
        var adjustment = t.Adjustments.Where(a => a.PlayerId == e.PlayerId).Sum(a => a.Points);
        return new Standing(e.PlayerId, e.Player.DisplayName, games.Count, seats.Count(s => s.Outcome == Outcome.Winner),
            games.Sum(g => g.Seats.Count(s => s.KillerId == e.PlayerId)), seats.Count(s => s.Outcome == Outcome.Survived),
            adjustment, games.Sum(g => Points(g, g.Seats.Single(s => s.PlayerId == e.PlayerId))) + adjustment);
    }).OrderByDescending(x => x.Points).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static string? Validate(IReadOnlyList<Seat> seats)
    {
        if (seats.Count < 2 || seats.Count > 8) return "Selecciona entre 2 y 8 jugadores para la mesa.";
        if (seats.Select(s => s.PlayerId).Distinct().Count() != seats.Count) return "Un jugador no puede aparecer dos veces.";
        if (seats.Any(s => !Enum.IsDefined(s.Outcome))) return "Hay un resultado inválido.";
        var winners = seats.Count(s => s.Outcome == Outcome.Winner);
        if (winners > 1) return "Solo puede haber un ganador de la mesa.";
        if (winners == 1 && seats.Any(s => s.Outcome == Outcome.Survived)) return "Si hay un ganador, los demás deben figurar como eliminados o retirados.";
        if (seats.Any(s => s.KillerId.HasValue && (s.Outcome != Outcome.Eliminated || s.KillerId == s.PlayerId || !seats.Any(p => p.PlayerId == s.KillerId))))
            return "La eliminación debe atribuirse a otro participante y solo a un jugador eliminado.";
        return null;
    }
}
