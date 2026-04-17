namespace Liga305.Domain;

/// <summary>
/// Glicko-2 rating system, exposed in the 1500-centered Glicko scale.
/// Ported from Mark Glickman's 2013 paper "Example of the Glicko-2 system".
///
/// For team matches: treat a win as N wins against each opponent's rating and
/// a loss as N losses against each teammate's opponent, using pre-match ratings.
/// </summary>
public static class Glicko2
{
    // System volatility constraint. 0.3–1.2 typical; lower = ratings move less over volatility updates.
    private const double Tau = 0.5;
    private const double GlickoToGlicko2Scale = 173.7178;

    public readonly record struct Rating(double Mu, double Phi, double Sigma);
    public readonly record struct GameResult(Rating Opponent, double Score);

    public static Rating FromGlicko(double mmr, double rd, double volatility) =>
        new((mmr - 1500.0) / GlickoToGlicko2Scale, rd / GlickoToGlicko2Scale, volatility);

    public static (double Mmr, double Rd, double Volatility) ToGlicko(Rating r) =>
        (r.Mu * GlickoToGlicko2Scale + 1500.0, r.Phi * GlickoToGlicko2Scale, r.Sigma);

    public static Rating Update(Rating player, IReadOnlyList<GameResult> games)
    {
        // Rating period with no games: inflate phi by inactivity.
        if (games.Count == 0)
        {
            var phi = Math.Sqrt(player.Phi * player.Phi + player.Sigma * player.Sigma);
            return new Rating(player.Mu, phi, player.Sigma);
        }

        double vInv = 0;
        double deltaSum = 0;
        foreach (var (opp, score) in games)
        {
            var gPhi = G(opp.Phi);
            var e = Expected(player.Mu, opp.Mu, opp.Phi);
            vInv += gPhi * gPhi * e * (1 - e);
            deltaSum += gPhi * (score - e);
        }
        var v = 1.0 / vInv;
        var delta = v * deltaSum;

        var sigmaNew = NewVolatility(player.Phi, player.Sigma, v, delta);
        var phiStar = Math.Sqrt(player.Phi * player.Phi + sigmaNew * sigmaNew);
        var phiNew = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
        var muNew = player.Mu + phiNew * phiNew * deltaSum;

        return new Rating(muNew, phiNew, sigmaNew);
    }

    private static double G(double phi) =>
        1.0 / Math.Sqrt(1 + 3 * phi * phi / (Math.PI * Math.PI));

    private static double Expected(double mu, double muOpp, double phiOpp) =>
        1.0 / (1 + Math.Exp(-G(phiOpp) * (mu - muOpp)));

    private static double NewVolatility(double phi, double sigma, double v, double delta)
    {
        const double epsilon = 1e-6;
        var a = Math.Log(sigma * sigma);

        double F(double x)
        {
            var ex = Math.Exp(x);
            var num = ex * (delta * delta - phi * phi - v - ex);
            var den = 2 * Math.Pow(phi * phi + v + ex, 2);
            return num / den - (x - a) / (Tau * Tau);
        }

        double A = a;
        double B;
        if (delta * delta > phi * phi + v)
        {
            B = Math.Log(delta * delta - phi * phi - v);
        }
        else
        {
            var k = 1;
            while (F(a - k * Tau) < 0) k++;
            B = a - k * Tau;
        }

        var fA = F(A);
        var fB = F(B);
        while (Math.Abs(B - A) > epsilon)
        {
            var C = A + (A - B) * fA / (fB - fA);
            var fC = F(C);
            if (fC * fB <= 0) { A = B; fA = fB; }
            else { fA /= 2; }
            B = C;
            fB = fC;
        }

        return Math.Exp(A / 2);
    }
}
