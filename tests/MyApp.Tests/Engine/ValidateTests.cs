namespace MyApp.Tests.Engine;

public class ValidateTests
{
    private readonly SlotEngine _engine = new();

    [Theory]
    [MemberData(nameof(Contract.AllBets), MemberType = typeof(Contract))]
    public void Validate_AcceptsEveryContractBet(decimal bet)
    {
        var session = new GameSession("t");

        Assert.Null(_engine.Validate(session, bet));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.15)]
    [InlineData(100)]
    public void Validate_RejectsBetsOutsideTheList(double bet)
    {
        // [InlineData] can't hold decimal constants, so the data is double and converted here.
        var session = new GameSession("t");

        Assert.Equal("Invalid bet.", _engine.Validate(session, (decimal)bet));
    }

    [Fact]
    public void Validate_RejectsMissingBet()
    {
        Assert.Equal("Invalid bet.", _engine.Validate(new GameSession("t"), null));
    }

    [Fact]
    public void Validate_RejectsBetAboveBalance()
    {
        var session = new GameSession("t") { Balance = 0.50m };

        Assert.Equal("Insufficient balance.", _engine.Validate(session, 1m));
    }

    [Fact]
    public void Spin_WithInvalidBet_ThrowsAndLeavesBalanceUntouched()
    {
        var session = new GameSession("t");

        Assert.Throws<InvalidOperationException>(() => _engine.Spin(session, 3m));
        Assert.Equal(Contract.StartingBalance, session.Balance);
    }

    [Theory]
    [MemberData(nameof(Contract.AllBets), MemberType = typeof(Contract))]
    public void BetEqualToBalance_IsAllowed_AndBalanceStaysNonNegative(decimal bet)
    {
        var session = new GameSession("t") { Balance = bet };

        Assert.Null(_engine.Validate(session, bet));
        var result = _engine.Spin(session, bet);

        Assert.Equal(result.TotalWin, result.State.Balance); // paid everything, kept only the win
        Assert.True(result.State.Balance >= 0);
    }

    [Fact]
    public void DuringFreeSpins_BetIsIgnored_EvenIfInvalidOrUnaffordable()
    {
        var session = Play.InFeature(freeSpins: 1, freeSpinBet: 2m);
        session.Balance = 0m;

        Assert.Null(_engine.Validate(session, 3m));
        Assert.Null(_engine.Validate(session, null));

        var result = _engine.Spin(session, 3m);

        Assert.True(result.WasFreeSpin);
        Assert.Equal(2m, result.Bet);
    }
}
