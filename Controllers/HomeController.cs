using CampeonatoMagic.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampeonatoMagic.Controllers;

public class HomeController(LeagueDb db) : Controller
{
    public async Task<IActionResult> Index(string? estado)
    {
        ViewBag.State = estado;
        var query = db.Tournaments.AsNoTracking().AsSplitQuery().Include(t => t.Champion).Include(t => t.Enrollments).Include(t => t.Games).AsQueryable();
        if (estado == "activos") query = query.Where(t => !t.Closed);
        if (estado == "finalizados") query = query.Where(t => t.Closed);
        return View(await query.OrderBy(t => t.Closed).ThenByDescending(t => t.StartDate).ToListAsync());
    }
    [HttpGet("campeonatos/{id:int}")]
    public async Task<IActionResult> Tournament(int id)
    {
        var t = await db.Tournaments.AsNoTracking().AsSplitQuery().Include(t => t.Champion)
            .Include(t => t.Enrollments).ThenInclude(e => e.Player)
            .Include(t => t.Games).ThenInclude(g => g.Seats).ThenInclude(s => s.Player)
            .Include(t => t.Games).ThenInclude(g => g.Seats).ThenInclude(s => s.Killer)
            .Include(t => t.Adjustments).ThenInclude(a => a.Player).SingleOrDefaultAsync(t => t.Id == id);
        return t is null ? NotFound() : View(t);
    }
    [HttpGet("jugadores")]
    public async Task<IActionResult> Players() => View(await db.Players.AsNoTracking().Where(p => !p.IsDeleted).OrderBy(p => p.Name).ToListAsync());
    [HttpGet("reglas")]
    public IActionResult Rules() => View();
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}
