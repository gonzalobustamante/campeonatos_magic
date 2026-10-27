using CampeonatoMagic.Models;
namespace CampeonatoMagic.Tests;
public class ScoringTests
{
    [Fact] public void WinnerGetsThreePlusEveryKill()
    {
        var game = new Game { Seats = [new() { PlayerId=1, Outcome=Outcome.Winner }, new() { PlayerId=2, KillerId=1 }, new() { PlayerId=3, KillerId=1 }, new() { PlayerId=4, KillerId=1 }] };
        Assert.Null(Scoring.Validate(game.Seats));
        Assert.Equal(6, Scoring.Points(game, game.Seats[0]));
        Assert.Equal(0, Scoring.Points(game, game.Seats[1]));
    }
    [Fact] public void LosingPlayerKeepsKillPoints()
    {
        var game = new Game { Seats = [new() { PlayerId=1, Outcome=Outcome.Winner }, new() { PlayerId=2, KillerId=1 }, new() { PlayerId=3, KillerId=2 }] };
        Assert.Equal(1, Scoring.Points(game, game.Seats[1]));
    }
    [Fact] public void DrawAwardsSurvivalAndKillsOnlyToRelevantPlayers()
    {
        var game = new Game { Seats = [new() { PlayerId=1, Outcome=Outcome.Survived }, new() { PlayerId=2, Outcome=Outcome.Survived }, new() { PlayerId=3, KillerId=1 }, new() { PlayerId=4, Outcome=Outcome.Conceded }] };
        Assert.Null(Scoring.Validate(game.Seats));
        Assert.Equal(2, Scoring.Points(game, game.Seats[0]));
        Assert.Equal(1, Scoring.Points(game, game.Seats[1]));
        Assert.Equal(0, Scoring.Points(game, game.Seats[3]));
    }
    [Theory]
    [InlineData(1, Outcome.Winner, null, 2, Outcome.Survived, null)]
    [InlineData(1, Outcome.Winner, null, 2, Outcome.Winner, null)]
    [InlineData(1, Outcome.Eliminated, 1, 2, Outcome.Winner, null)]
    [InlineData(1, Outcome.Eliminated, 9, 2, Outcome.Winner, null)]
    [InlineData(1, Outcome.Survived, 2, 2, Outcome.Eliminated, null)]
    [InlineData(1, (Outcome)999, null, 2, Outcome.Eliminated, null)]
    public void RejectsInconsistentOutcomes(int a, Outcome aResult, int? killer, int b, Outcome bResult, int? bKiller)
    {
        Assert.NotNull(Scoring.Validate([new() { PlayerId=a, Outcome=aResult, KillerId=killer }, new() { PlayerId=b, Outcome=bResult, KillerId=bKiller }]));
    }
    [Fact] public void TablesStayIsolatedAndIncludeManualAdjustments()
    {
        var t = new Tournament { Enrollments=[new() {PlayerId=1,Player=new(){Id=1,Name="Ana"}},new() {PlayerId=2,Player=new(){Id=2,Name="Beto"}}],
          Games=[new(){Seats=[new(){PlayerId=1,Outcome=Outcome.Winner},new(){PlayerId=2,KillerId=1}]}],
          Adjustments=[new(){PlayerId=1,Points=-2},new(){PlayerId=2,Points=5}] };
        var table = Scoring.Table(t);
        Assert.Equal(2,table[0].PlayerId); Assert.Equal(5,table[0].Points);
        Assert.Equal(2,table[1].Points);
        Assert.Empty(Scoring.Table(new Tournament()));
    }
}
