using System.Net;
using System.Text.RegularExpressions;
using CampeonatoMagic.Models;
using CampeonatoMagic.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampeonatoMagic.Tests;
public class WorkflowTests
{
    private sealed class App : WebApplicationFactory<Program>
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "magic-test-" + Guid.NewGuid());
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development").UseSetting("DataDirectory", Folder);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }
    private static async Task<string> Token(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string page, string target, params (string Key,string Value)[] pairs)
    {
        var values = pairs.Select(p => new KeyValuePair<string,string>(p.Key,p.Value)).ToList();
        values.Add(new("__RequestVerificationToken", await Token(client,page)));
        return await client.PostAsync(target,new FormUrlEncodedContent(values));
    }
    private static async Task Login(HttpClient client) =>
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/cuenta/entrar","/cuenta/entrar",("Username","organizador"),("Password","test-only-password-123"))).StatusCode);

    [Fact] public async Task DeletePlayerRemovesRegistrationButPreservesScoresAndHistory()
    {
        using var app = new App();
        using var client = app.CreateClient(new() { AllowAutoRedirect=false });
        app.Services.GetRequiredService<AdminAccount>().Set("organizador","test-only-password-123");
        int playerId, rivalId, leagueId, emptyId, closedId, gameId;
        const string playerName = "Jugador por eliminar";
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeagueDb>();
            var player = new Player { Name = playerName }; var rival = new Player { Name = "Rival histórico" };
            db.Players.AddRange(player,rival); await db.SaveChangesAsync();
            playerId = player.Id; rivalId = rival.Id;
            var league = new Tournament { Name="Con historial", Enrollments=[new(){PlayerId=player.Id},new(){PlayerId=rival.Id}],
                Games=[new(){PlayedAt=DateTime.Now, Seats=[new(){PlayerId=player.Id,Outcome=Outcome.Winner},new(){PlayerId=rival.Id,Outcome=Outcome.Eliminated,KillerId=player.Id}]}],
                Adjustments=[new(){PlayerId=rival.Id,Points=2,Reason="Ajuste histórico"}] };
            var empty = new Tournament { Name="Sin partidas", Enrollments=[new(){PlayerId=player.Id},new(){PlayerId=rival.Id}] };
            var closed = new Tournament { Name="Campeón histórico", Closed=true, ChampionId=player.Id, Enrollments=[new(){PlayerId=player.Id}] };
            db.Tournaments.AddRange(league,empty,closed); await db.SaveChangesAsync();
            leagueId=league.Id; emptyId=empty.Id; closedId=closed.Id; gameId=league.Games.Single().Id;
        }
        // The endpoint stays protected even when a player has history.
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/admin/jugador/{playerId}/eliminar",new FormUrlEncodedContent([]))).StatusCode);
        await Login(client);
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/jugadores",$"/admin/jugador/{playerId}/eliminar")).StatusCode);
        var registry=await client.GetStringAsync("/jugadores");
        Assert.DoesNotContain(playerName,registry);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/admin/jugador/{playerId}")).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<LeagueDb>();
            var deleted=await db.Players.FindAsync(playerId);
            Assert.True(deleted!.IsDeleted); Assert.False(deleted.Active);
            Assert.False(await db.Enrollments.AnyAsync(e=>e.TournamentId==emptyId&&e.PlayerId==playerId));
            Assert.True(await db.Enrollments.AnyAsync(e=>e.TournamentId==leagueId&&e.PlayerId==playerId));
            Assert.Equal(playerId,(await db.Tournaments.FindAsync(closedId))!.ChampionId);
            var t=await db.Tournaments.Include(t=>t.Enrollments).ThenInclude(e=>e.Player).Include(t=>t.Games).ThenInclude(g=>g.Seats).Include(t=>t.Adjustments).SingleAsync(t=>t.Id==leagueId);
            Assert.Equal(4,Scoring.Table(t).Single(s=>s.PlayerId==playerId).Points);
            Assert.Equal(2,Scoring.Table(t).Single(s=>s.PlayerId==rivalId).Points);
        }
        Assert.DoesNotContain(playerName,await client.GetStringAsync($"/admin/partida/{leagueId}"));
        Assert.Contains(playerName,await client.GetStringAsync($"/admin/partida/{leagueId}/{gameId}"));
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/admin/jugador","/admin/jugador",("Name",playerName),("Active","true"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/",$"/admin/campeonato/{leagueId}/eliminar")).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<LeagueDb>();
            Assert.False(await db.Games.AnyAsync(g=>g.TournamentId==leagueId));
            Assert.False(await db.Adjustments.AnyAsync(a=>a.TournamentId==leagueId));
            Assert.Equal(2,await db.Tournaments.CountAsync());
        }
    }

    [Fact] public async Task PublicReadAdminCrudAndTournamentLifecycle()
    {
        using var app = new App();
        using var client = app.CreateClient(new() { AllowAutoRedirect=false });
        app.Services.GetRequiredService<AdminAccount>().Set("organizador","test-only-password-123");
        foreach(var path in new[]{"/","/jugadores","/reglas","/cuenta/entrar"})
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(path)).StatusCode);
        var readiness = await client.GetStringAsync("/health/ready");
        Assert.Contains("\"status\":\"ready\"", readiness);
        Assert.Contains("\"commit\":\"local\"", readiness);
        var adminPage = await client.GetAsync("/admin/campeonato");
        Assert.Equal(HttpStatusCode.Redirect,adminPage.StatusCode);
        Assert.Equal("/cuenta/entrar",adminPage.Headers.Location!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/admin/campeonato/999/eliminar", new FormUrlEncodedContent([]))).StatusCode);
        await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/admin/jugador",new FormUrlEncodedContent([new("Name","No CSRF")]))).StatusCode);
        var emptyPlayer = await Post(client,"/admin/jugador","/admin/jugador",("Name",""),("Active","true"));
        Assert.Equal(HttpStatusCode.OK,emptyPlayer.StatusCode);
        Assert.Contains("validation-summary-errors",await emptyPlayer.Content.ReadAsStringAsync());
        foreach (var name in new[]{"Ana","Beto","Cata","Diego"})
            Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/admin/jugador","/admin/jugador",("Name",name),("Active","true"))).StatusCode);
        int[] players;
        using (var scope = app.Services.CreateScope()) players = await scope.ServiceProvider.GetRequiredService<LeagueDb>().Players.OrderBy(p=>p.Id).Select(p=>p.Id).ToArrayAsync();
        var create = new List<(string,string)> { ("Name","Liga de prueba"), ("Description",""),("StartDate","2026-10-26") };
        create.AddRange(players.Select(id=>("PlayerIds",id.ToString())));
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/admin/campeonato","/admin/campeonato",create.ToArray())).StatusCode);
        int tournament;
        using(var scope=app.Services.CreateScope()) tournament=await scope.ServiceProvider.GetRequiredService<LeagueDb>().Tournaments.Select(t=>t.Id).SingleAsync();
        var gamePage=$"/admin/partida/{tournament}";
        var gameForm = new List<(string,string)> { ("TournamentId",tournament.ToString()),("PlayedAt","2026-10-26T19:00"),("Notes","") };
        for(var i=0;i<players.Length;i++) gameForm.AddRange(new[]{($"Seats[{i}].PlayerId",players[i].ToString()),($"Seats[{i}].Selected","true"),($"Seats[{i}].Outcome",i==0?"Winner":"Eliminated"),($"Seats[{i}].KillerId",i==0?"":players[0].ToString())});
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,gamePage,gamePage,gameForm.ToArray())).StatusCode);
        int game;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<LeagueDb>();
            var t=await db.Tournaments.Include(t=>t.Enrollments).ThenInclude(e=>e.Player).Include(t=>t.Games).ThenInclude(g=>g.Seats).Include(t=>t.Adjustments).SingleAsync();
            Assert.Equal(6,Scoring.Table(t)[0].Points);
            game=t.Games.Single().Id;
        }
        // Editing replaces the entire result transactionally, without duplicated seats or stale points.
        gameForm.Add(("Id",game.ToString()));
        gameForm.RemoveAll(p=>p.Item1.EndsWith(".KillerId"));
        gameForm.RemoveAll(p=>p.Item1.EndsWith(".Outcome"));
        for(var i=0;i<players.Length;i++) gameForm.Add(($"Seats[{i}].Outcome","Survived"));
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,gamePage+"/"+game,gamePage+"/"+game,gameForm.ToArray())).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<LeagueDb>();
            Assert.Equal(4,await db.Seats.CountAsync());
            var g=await db.Games.Include(g=>g.Seats).SingleAsync();
            Assert.All(g.Seats,s=>Assert.Equal(1,Scoring.Points(g,s)));
        }
        var detail=$"/campeonatos/{tournament}";
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(detail)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,detail,$"/admin/campeonato/{tournament}/ajuste",("playerId",players[1].ToString()),("points","2"),("reason","Corrección documentada"))).StatusCode);
        // Champion can be selected independently from the score leader.
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,detail,$"/admin/campeonato/{tournament}/cerrar",("championId",players[0].ToString()))).StatusCode);
        await Post(client,detail,$"/admin/partida/{game}/eliminar");
        using(var scope=app.Services.CreateScope()) Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<LeagueDb>().Games.CountAsync());
        await Post(client,detail,$"/admin/campeonato/{tournament}/reabrir");
        Assert.Equal(HttpStatusCode.Redirect,(await Post(client,detail,$"/admin/campeonato/{tournament}/eliminar")).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<LeagueDb>();
            Assert.Empty(await db.Games.ToListAsync()); Assert.Empty(await db.Seats.ToListAsync());
            Assert.Empty(await db.Adjustments.ToListAsync()); Assert.Empty(await db.Enrollments.ToListAsync());
            Assert.Equal(4,await db.Players.CountAsync());
        }
        app.Services.GetRequiredService<AdminAccount>().Set("organizador","new-test-password-123");
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/admin/campeonato")).StatusCode);
    }
}
