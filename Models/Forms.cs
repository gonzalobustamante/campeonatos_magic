using System.ComponentModel.DataAnnotations;

namespace CampeonatoMagic.Models;

public class TournamentForm
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Escribe el nombre del campeonato."), StringLength(100)] public string Name { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public List<int> PlayerIds { get; set; } = [];
    public List<Player> Players { get; set; } = [];
}

public class SeatForm
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
    public bool Selected { get; set; }
    public Outcome Outcome { get; set; } = Outcome.Eliminated;
    public int? KillerId { get; set; }
}

public class GameForm
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = "";
    public DateTime PlayedAt { get; set; } = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Santiago");
    [StringLength(1000)] public string? Notes { get; set; }
    public List<SeatForm> Seats { get; set; } = [];
}

public class LoginForm
{
    [Required] public string Username { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
