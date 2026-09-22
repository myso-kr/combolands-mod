using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Combolands.Mod.Autoplay.Sim;

namespace Combolands.Train
{
    // Fitting the placement policy by playing milestones.
    //
    // The method is the cross-entropy method, and the reason is the shape of the
    // problem rather than fashion. There are six parameters; the objective is an
    // average over noisy episodes with no usable gradient, because a placement is an
    // argmax over thousands of tiles and moving a weight a little usually changes
    // nothing and occasionally changes everything. Policy gradients need that
    // derivative. CEM does not: it samples policies, keeps the ones that cleared the
    // most milestones, and fits the next generation's distribution to those.
    //
    // It is also legible. Every generation prints what it kept and how wide the
    // distribution still is, so a run that is not converging says so rather than
    // producing six numbers with no provenance.
    //
    //   dotnet run --project tools/train
    //   dotnet run --project tools/train -- --generations 40 --population 60
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = Options.Parse(args);
            if (options == null) return 2;

            var rulesPath = options.Rules ?? Path.Combine(Repo(), "generated", "rules.json");
            if (!File.Exists(rulesPath))
            {
                Console.Error.WriteLine(
                    "no rule set at " + rulesPath + "\n\n" +
                    "It is dumped out of the game and is not in this repository. Set\n" +
                    "DumpRules = true in UserData/MelonPreferences.cfg, start a run, and copy\n" +
                    "UserData/Combolands/generated/rules.json here.");
                return 1;
            }

            var rules = Rules.Parse(File.ReadAllText(rulesPath));
            var pool = Scenario.ScoringPool(rules);
            var targets = Scenario.TargetPool(rules);

            Console.WriteLine("{0} buildings - {1} exact, {2} approximate. {3} worth offering, {4} worth scoring off.",
                rules.Count, rules.CountOf(Fidelity.Exact), rules.CountOf(Fidelity.Approximate),
                pool.Length, targets.Length);

            if (pool.Length == 0)
            {
                Console.Error.WriteLine("no building in the rule set scores anything - the dump is wrong");
                return 1;
            }

            // The same scenarios for every policy in a generation, and for the
            // baseline. Two policies judged on different milestones are not being
            // compared, they are being sampled - and with episodes this noisy that
            // difference decides whether training converges at all.
            var world = new Random(options.Seed);
            var scenarios = Enumerable.Range(0, options.Episodes)
                                      .Select(_ => Scenario.Sample(rules, pool, targets, world))
                                      .ToArray();

            Console.WriteLine("{0} milestones, {1} generations of {2}\n",
                scenarios.Length, options.Generations, options.Population);

            var start = Policy.Default();
            var baseline = Judge(start, scenarios, options.Seed);
            Console.WriteLine("baseline (hand-written): {0}", baseline);
            Console.WriteLine("  {0}\n", start);

            var best = Fit(scenarios, options, start, baseline);

            var final = Judge(best, scenarios, options.Seed);
            Console.WriteLine("\nfitted, on the milestones it was fitted to: {0}", final);
            Console.WriteLine("  {0}", best);

            // That number is optimistic, and saying so is arithmetic rather than
            // modesty: those are the scenarios the search was allowed to look at, and
            // across several hundred sampled policies some of the gain is the search
            // finding their quirks rather than learning the game. The comparison that
            // decides whether to ship is on milestones neither policy has seen.
            var holdout = Holdout(rules, pool, targets, options);
            var heldBase = Judge(start, holdout, options.Seed + Fresh);
            var heldFinal = Judge(best, holdout, options.Seed + Fresh);

            Console.WriteLine("\non {0} milestones neither has seen:", holdout.Length);
            Console.WriteLine("  hand-written: {0}", heldBase);
            Console.WriteLine("  fitted:       {0}", heldFinal);
            Console.WriteLine("  difference:   {0:+0.0%;-0.0%;none} cleared, {1:+0.0%;-0.0%;none} margin",
                heldFinal.ClearRate - heldBase.ClearRate, heldFinal.Margin - heldBase.Margin);

            // Refusing to write a policy that is worse than the one already shipped is
            // the whole safety net here, and it is judged on the HELD-OUT milestones.
            // Training is stochastic, a bad seed is a real outcome, and a policy that
            // only looks better on the scenarios it was fitted to has learned their
            // quirks rather than the game. Either way, overwriting good weights would
            // be a silent regression that surfaces days later as a missed milestone.
            if (heldFinal.Score < heldBase.Score)
            {
                Console.Error.WriteLine(
                    "\nOn milestones it was not fitted to, the fitted policy is no better than\n" +
                    "the hand-written one. Nothing was written - that is overfitting, not\n" +
                    "improvement. Try more episodes, or accept that the shape is already right.");
                return 1;
            }

            var output = options.Output ?? Path.Combine(Repo(), "locale", "policy", "weights.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, best.ToJson());
            Console.WriteLine("\nwrote {0}", output);

            return 0;
        }

        // Far enough from the training seed that the two sets share nothing. One
        // `Random`, walked, so the holdout is as varied as the training set rather
        // than the same board sampled repeatedly.
        private const int Fresh = 99991;

        private static Scenario[] Holdout(Rules rules, int[] pool, int[] targets, Options options)
        {
            var world = new Random(options.Seed + Fresh);
            return Enumerable.Range(0, options.Episodes)
                             .Select(_ => Scenario.Sample(rules, pool, targets, world))
                             .ToArray();
        }

        // --- the cross-entropy method ---------------------------------------------

        private static Policy Fit(Scenario[] scenarios, Options options, Policy start, Result baseline)
        {
            var mean = start.ToVector();
            var deviation = Enumerable.Repeat(options.Spread, Policy.Size).ToArray();

            var best = start;
            var bestRate = baseline.ClearRate;

            var random = new Random(options.Seed + 1);
            var clock = Stopwatch.StartNew();

            for (int generation = 1; generation <= options.Generations; generation++)
            {
                var population = new float[options.Population][];
                for (int i = 0; i < population.Length; i++)
                    population[i] = Perturb(mean, deviation, random);

                // Episodes are independent and there are thousands of them per
                // generation; this is the one place parallelism is free.
                var scored = new Result[population.Length];
                Parallel.For(0, population.Length, i =>
                {
                    scored[i] = Judge(Policy.FromVector(population[i]), scenarios, options.Seed);
                });

                // Keep the best tenth, refit the distribution to them.
                var keep = Math.Max(2, population.Length / 10);
                var elite = Enumerable.Range(0, population.Length)
                                      .OrderByDescending(i => scored[i].Score)
                                      .Take(keep)
                                      .ToArray();

                for (int w = 0; w < Policy.Size; w++)
                {
                    var values = elite.Select(i => (double)population[i][w]).ToArray();
                    var m = values.Average();
                    var v = values.Select(x => (x - m) * (x - m)).Average();

                    mean[w] = (float)m;

                    // A floor under the spread, or the distribution collapses onto
                    // the first decent policy it finds and stops exploring - which
                    // looks exactly like convergence and is not.
                    deviation[w] = (float)Math.Max(Math.Sqrt(v), options.Floor);
                }

                var leader = scored[elite[0]];
                if (leader.ClearRate > bestRate)
                {
                    bestRate = leader.ClearRate;
                    best = Policy.FromVector(population[elite[0]]);
                }

                Console.WriteLine("gen {0,3}  best {1}  spread {2:0.000}  {3:0.0}s",
                    generation, leader, deviation.Average(), clock.Elapsed.TotalSeconds);
            }

            // The mean of the final distribution is usually better than any single
            // sample from it - it is the average of what worked - but not always, so
            // it has to earn the place.
            var settled = Policy.FromVector(mean);
            return Judge(settled, scenarios, options.Seed).ClearRate >= bestRate ? settled : best;
        }

        private static float[] Perturb(float[] mean, float[] deviation, Random random)
        {
            var sample = new float[mean.Length];
            for (int i = 0; i < mean.Length; i++)
                sample[i] = mean[i] + deviation[i] * (float)Gaussian(random);
            return sample;
        }

        private static double Gaussian(Random random)
        {
            // Box-Muller. Random.NextDouble can return exactly 0 and log(0) is not a
            // number anyone wants in a weight vector.
            var u1 = 1.0 - random.NextDouble();
            var u2 = 1.0 - random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        // --- what a policy is worth ------------------------------------------------

        internal struct Result
        {
            public double ClearRate;
            public double Margin;
            public int Cleared;
            public int Total;

            // Clearing is what matters, so it carries the score. Margin breaks ties
            // and, more importantly, gives the search something to climb while no
            // policy in the population clears anything at all - without it the first
            // generations are a flat landscape and CEM has nothing to select on.
            public double Score { get { return ClearRate + 0.05 * Margin; } }

            public override string ToString()
            {
                return string.Format("{0}/{1} cleared ({2:0.0%}), margin {3:+0.0%;-0.0%;0%}",
                                     Cleared, Total, ClearRate, Margin);
            }
        }

        private static Result Judge(Policy policy, Scenario[] scenarios, int seed)
        {
            var result = new Result { Total = scenarios.Length };
            double margin = 0;

            for (int i = 0; i < scenarios.Length; i++)
            {
                var scenario = scenarios[i];

                // A fresh board each time: Play places buildings, and an episode that
                // inherited the last one's board would be measuring the wrong thing.
                var milestone = new Milestone(scenario.Milestone.Rules,
                                              scenario.Milestone.Map.Clone(),
                                              scenario.Milestone.Required,
                                              scenario.Milestone.Weeks,
                                              scenario.Milestone.Pool);

                // Seeded per scenario, so every policy sees the same cards in the same
                // order and the comparison is about play rather than luck.
                var outcome = milestone.Play(policy, new Random(seed * 7919 + i));

                if (outcome.Cleared) result.Cleared++;

                // Clamped: a policy that overshoots one easy milestone by 900% should
                // not outrank one that quietly clears everything.
                margin += Math.Max(-1.0, Math.Min(1.0, outcome.Margin));
            }

            result.ClearRate = (double)result.Cleared / result.Total;
            result.Margin = margin / result.Total;
            return result;
        }

        // --- plumbing ---------------------------------------------------------------

        private sealed class Options
        {
            public int Generations = 25;
            public int Population = 48;
            public int Episodes = 60;
            public int Seed = 20260923;
            public float Spread = 0.6f;
            public float Floor = 0.05f;
            public string Rules;
            public string Output;

            public static Options Parse(string[] args)
            {
                var options = new Options();

                for (int i = 0; i < args.Length; i++)
                {
                    var name = args[i];
                    Func<string> value = () => ++i < args.Length ? args[i] : null;

                    switch (name)
                    {
                        case "--generations": options.Generations = int.Parse(value(), CultureInfo.InvariantCulture); break;
                        case "--population":  options.Population = int.Parse(value(), CultureInfo.InvariantCulture); break;
                        case "--episodes":    options.Episodes = int.Parse(value(), CultureInfo.InvariantCulture); break;
                        case "--seed":        options.Seed = int.Parse(value(), CultureInfo.InvariantCulture); break;
                        case "--spread":      options.Spread = float.Parse(value(), CultureInfo.InvariantCulture); break;
                        case "--rules":       options.Rules = value(); break;
                        case "--out":         options.Output = value(); break;
                        case "-h": case "--help":
                            Console.WriteLine(
                                "usage: dotnet run --project tools/train -- [options]\n" +
                                "  --generations N   how many rounds of selection (default 25)\n" +
                                "  --population N    policies sampled per round (default 48)\n" +
                                "  --episodes N      milestones each policy plays (default 60)\n" +
                                "  --seed N          makes a run reproducible\n" +
                                "  --spread F        initial standard deviation (default 0.6)\n" +
                                "  --rules PATH      the dumped rule set\n" +
                                "  --out PATH        where to write the fitted weights");
                            return null;
                        default:
                            Console.Error.WriteLine("unknown option " + name);
                            return null;
                    }
                }

                return options;
            }
        }

        private static string Repo()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "Combolands.Mod")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
