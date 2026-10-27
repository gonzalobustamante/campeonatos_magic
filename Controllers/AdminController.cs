using CampeonatoMagic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampeonatoMagic.Controllers;

[Authorize, Route("admin")]
public class AdminController(LeagueDb db) : Controller
{
    private IActionResult Go(int id) => RedirectToAction("Tournament", "Home", new { id });
    private IActionResult Notice(string message, int? id = null)
    {
        TempData["Notice"] = message;
        return id.HasValue ? Go(id.Value) : RedirectToAction("Index", "Home");
    }

    [HttpGet("campeonato/{id:int?}")]
    public async Task<IActionResult> Tournament(int? id)
    {
        var form = new TournamentForm();
        if (id.HasValue)
        {
            var t = await db.Tournaments.Include(t => t.Enrollments).SingleOrDefaultAsync(t => t.Id == id);
            if (t is null) return NotFound();
            if (t.Closed) return Notice("Reabre el campeonato antes de editarlo.", t.Id);
            form = new() { Id = t.Id, Name = t.Name, Description = t.Description, StartDate = t.StartDate, PlayerIds = t.Enrollments.Select(e => e.PlayerId).ToList() };
        }
        form.Players = await db.Players.Where(p => !p.IsDeleted || db.Enrollments.Any(e => e.TournamentId == form.Id && e.PlayerId == p.Id)).OrderBy(p => p.Name).ToListAsync();
        return View(form);
    }

    [HttpPost("campeonato/{id:int?}")]
    public async Task<IActionResult> Tournament(TournamentForm form)
    {
        form.Players = await db.Players.Where(p => !p.IsDeleted || db.Enrollments.Any(e => e.TournamentId == form.Id && e.PlayerId == p.Id)).OrderBy(p => p.Name).ToListAsync();
        form.PlayerIds = form.PlayerIds.Distinct().ToList();
        if (form.PlayerIds.Except(form.Players.Select(p => p.Id)).Any()) ModelState.AddModelError("", "Selecciona jugadores válidos.");
        if (form.StartDate == default) ModelState.AddModelError("", "Indica la fecha de inicio.");
        if (!ModelState.IsValid) return View(form);
        var t = form.Id == 0 ? new Models.Tournament() : await db.Tournaments.Include(t => t.Enrollments).SingleOrDefaultAsync(t => t.Id == form.Id);
        if (t is null) return NotFound();
        if (t.Closed) return Notice("Reabre el campeonato antes de editarlo.", t.Id);
        var used = await db.Seats.Where(s => s.Game.TournamentId == t.Id).Select(s => s.PlayerId)
            .Union(db.Adjustments.Where(a => a.TournamentId == t.Id).Select(a => a.PlayerId)).Distinct().ToListAsync();
        if (used.Except(form.PlayerIds).Any())
        {
            ModelState.AddModelError("", "No puedes quitar jugadores que tengan partidas o ajustes registrados en este campeonato.");
            return View(form);
        }
        t.Name = form.Name.Trim(); t.Description = (form.Description ?? "").Trim(); t.StartDate = form.StartDate;
        foreach (var removed in t.Enrollments.Where(e => !form.PlayerIds.Contains(e.PlayerId)).ToList()) db.Enrollments.Remove(removed);
        foreach (var added in form.PlayerIds.Except(t.Enrollments.Select(e => e.PlayerId))) t.Enrollments.Add(new Enrollment { PlayerId = added });
        if (t.Id == 0) db.Tournaments.Add(t);
        await db.SaveChangesAsync();
        return Notice("Campeonato guardado.", t.Id);
    }

    [HttpPost("campeonato/{id:int}/eliminar")]
    public async Task<IActionResult> DeleteTournament(int id)
    {
        var t = await db.Tournaments.FindAsync(id);
        if (t is null) return NotFound();
        db.Tournaments.Remove(t); await db.SaveChangesAsync();
        return Notice("Campeonato eliminado junto con sus partidas y ajustes.");
    }

    [HttpPost("campeonato/{id:int}/cerrar")]
    public async Task<IActionResult> Close(int id, int championId)
    {
        var t = await db.Tournaments.Include(t => t.Enrollments).SingleOrDefaultAsync(t => t.Id == id);
        if (t is null) return NotFound();
        if (t.Closed) return Go(id);
        if (!t.Enrollments.Any(e => e.PlayerId == championId) || !await db.Games.AnyAsync(g => g.TournamentId == id))
            return Notice("Registra al menos una partida y elige un campeón entre los participantes.", id);
        t.Closed = true; t.ChampionId = championId; await db.SaveChangesAsync();
        return Notice("Campeonato finalizado. El primer lugar quedó registrado.", id);
    }

    [HttpPost("campeonato/{id:int}/reabrir")]
    public async Task<IActionResult> Reopen(int id)
    {
        var t = await db.Tournaments.FindAsync(id);
        if (t is null) return NotFound();
        t.Closed = false; t.ChampionId = null; await db.SaveChangesAsync();
        return Notice("Campeonato reabierto.", id);
    }

    [HttpGet("jugador/{id:int?}")]
    public async Task<IActionResult> Player(int? id)
    {
        var p = id.HasValue ? await db.Players.FindAsync(id.Value) : new Player();
        return p is null || p.IsDeleted ? NotFound() : View(p);
    }
    [HttpPost("jugador/{id:int?}")]
    public async Task<IActionResult> Player([Bind("Id,Name,Active")] Player form)
    {
        form.Name = (form.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(form.Name)) ModelState.AddModelError("Name", "Escribe un nombre.");
        var names = await db.Players.Where(p => !p.IsDeleted && p.Id != form.Id).Select(p => p.Name).ToListAsync();
        if (names.Any(n => string.Equals(n, form.Name, StringComparison.OrdinalIgnoreCase))) ModelState.AddModelError("Name", "Ya existe un jugador con ese nombre.");
        if (!ModelState.IsValid) return View(form);
        var p = form.Id == 0 ? new Player() : await db.Players.FindAsync(form.Id);
        if (p is null || p.IsDeleted) return NotFound();
        p.Name = form.Name; p.Active = form.Active;
        if (p.Id == 0) db.Players.Add(p);
        await db.SaveChangesAsync();
        TempData["Notice"] = "Jugador guardado.";
        return RedirectToAction("Players", "Home");
    }
    [HttpPost("jugador/{id:int}/eliminar")]
    public async Task<IActionResult> DeletePlayer(int id)
    {
        var p = await db.Players.FindAsync(id);
        if (p is null || p.IsDeleted) return NotFound();
        // Keep historical foreign keys and scores intact, but remove unused enrollments.
        var historyIds = await db.Seats.Where(s => s.PlayerId == id).Select(s => s.Game.TournamentId)
            .Union(db.Adjustments.Where(a => a.PlayerId == id).Select(a => a.TournamentId))
            .Union(db.Tournaments.Where(t => t.ChampionId == id).Select(t => t.Id)).ToListAsync();
        db.Enrollments.RemoveRange(await db.Enrollments.Where(e => e.PlayerId == id && !historyIds.Contains(e.TournamentId)).ToListAsync());
        p.IsDeleted = true; p.Active = false;
        await db.SaveChangesAsync();
        TempData["Notice"] = "Jugador eliminado del registro. Sus partidas anteriores conservan el historial y los puntos.";
        return RedirectToAction("Players", "Home");
    }

    [HttpGet("partida/{tournamentId:int}/{id:int?}")]
    public async Task<IActionResult> Game(int tournamentId, int? id)
    {
        var t = await db.Tournaments.Include(t => t.Enrollments).ThenInclude(e => e.Player).SingleOrDefaultAsync(t => t.Id == tournamentId);
        if (t is null) return NotFound();
        if (t.Closed) return Notice("Reabre el campeonato antes de registrar o editar partidas.", t.Id);
        var g = id.HasValue ? await db.Games.Include(g => g.Seats).SingleOrDefaultAsync(g => g.Id == id && g.TournamentId == tournamentId) : null;
        if (id.HasValue && g is null) return NotFound();
        return View(new GameForm
        {
            Id = g?.Id ?? 0, TournamentId = t.Id, TournamentName = t.Name,
            PlayedAt = g?.PlayedAt ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Santiago"), Notes = g?.Notes ?? "",
            Seats = t.Enrollments.Where(e => !e.Player.IsDeleted || g?.Seats.Any(s => s.PlayerId == e.PlayerId) == true).OrderBy(e => e.Player.Name).Select(e =>
            {
                var seat = g?.Seats.SingleOrDefault(s => s.PlayerId == e.PlayerId);
                return new SeatForm { PlayerId = e.PlayerId, Name = e.Player.DisplayName, Selected = seat is not null, Outcome = seat?.Outcome ?? Outcome.Eliminated, KillerId = seat?.KillerId };
            }).ToList()
        });
    }

    [HttpPost("partida/{tournamentId:int}/{id:int?}")]
    public async Task<IActionResult> Game(GameForm form)
    {
        var t = await db.Tournaments.Include(t => t.Enrollments).ThenInclude(e => e.Player).SingleOrDefaultAsync(t => t.Id == form.TournamentId);
        if (t is null) return NotFound();
        if (t.Closed) return Notice("Reabre el campeonato antes de modificar sus partidas.", t.Id);
        var game = form.Id == 0 ? new Game { TournamentId = t.Id } : await db.Games.Include(g => g.Seats).SingleOrDefaultAsync(g => g.Id == form.Id && g.TournamentId == t.Id);
        if (game is null) return NotFound();
        form.TournamentName = t.Name;
        var participants = t.Enrollments.ToDictionary(e => e.PlayerId, e => e.Player.Name);
        foreach (var s in form.Seats) s.Name = participants.GetValueOrDefault(s.PlayerId, "Jugador inválido");
        var seats = form.Seats.Where(s => s.Selected).Select(s => new Seat { PlayerId = s.PlayerId, Outcome = s.Outcome, KillerId = s.KillerId }).ToList();
        var error = Scoring.Validate(seats);
        if (error is not null) ModelState.AddModelError("", error);
        if (seats.Any(s => !participants.ContainsKey(s.PlayerId))) ModelState.AddModelError("", "Todos deben estar inscritos en este campeonato.");
        if (seats.Any(s => t.Enrollments.Any(e => e.PlayerId == s.PlayerId && e.Player.IsDeleted) && !game.Seats.Any(old => old.PlayerId == s.PlayerId)))
            ModelState.AddModelError("", "Un jugador eliminado solo puede conservarse en sus partidas anteriores.");
        if (form.PlayedAt == default) ModelState.AddModelError("", "Indica una fecha y hora válidas.");
        if (!ModelState.IsValid) return View(form);
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (game.Id != 0)
        {
            db.Seats.RemoveRange(game.Seats);
            await db.SaveChangesAsync();
        }
        game.PlayedAt = DateTime.SpecifyKind(form.PlayedAt, DateTimeKind.Unspecified); game.Notes = (form.Notes ?? "").Trim(); game.Seats = seats;
        if (game.Id == 0) db.Games.Add(game);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Notice("Partida guardada. La clasificación está actualizada.", t.Id);
    }

    [HttpPost("partida/{id:int}/eliminar")]
    public async Task<IActionResult> DeleteGame(int id)
    {
        var g = await db.Games.Include(g => g.Tournament).SingleOrDefaultAsync(g => g.Id == id);
        if (g is null) return NotFound();
        if (g.Tournament.Closed) return Notice("Reabre el campeonato antes de eliminar partidas.", g.TournamentId);
        db.Games.Remove(g); await db.SaveChangesAsync();
        return Notice("Partida eliminada. La clasificación está actualizada.", g.TournamentId);
    }

    [HttpPost("campeonato/{id:int}/ajuste")]
    public async Task<IActionResult> Adjust(int id, int playerId, int points, string? reason)
    {
        var t = await db.Tournaments.FindAsync(id);
        if (t is null) return NotFound();
        if (t.Closed) return Notice("Reabre el campeonato antes de ajustar puntos.", id);
        if (!ModelState.IsValid || points == 0 || points < -1000 || points > 1000 || string.IsNullOrWhiteSpace(reason) || reason.Length > 250 || !await db.Enrollments.AnyAsync(e => e.TournamentId == id && e.PlayerId == playerId))
            return Notice("Selecciona un participante, un ajuste entre −1000 y 1000 distinto de cero y su motivo (hasta 250 caracteres).", id);
        db.Adjustments.Add(new Adjustment { TournamentId = id, PlayerId = playerId, Points = points, Reason = reason.Trim() });
        await db.SaveChangesAsync(); return Notice("Ajuste de puntos registrado.", id);
    }
    [HttpPost("ajuste/{id:int}/eliminar")]
    public async Task<IActionResult> DeleteAdjustment(int id)
    {
        var a = await db.Adjustments.Include(a => a.Tournament).SingleOrDefaultAsync(a => a.Id == id);
        if (a is null) return NotFound();
        if (a.Tournament.Closed) return Notice("Reabre el campeonato antes de eliminar ajustes.", a.TournamentId);
        db.Adjustments.Remove(a); await db.SaveChangesAsync();
        return Notice("Ajuste eliminado.", a.TournamentId);
    }
}
