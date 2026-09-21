using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OrderPricing;

// Task 1 - the starter code, kept unchanged as debugging evidence. D1 adds instead of
// multiplying, D2 can never reach the 10% branch, D3 ignores isEmpty (and compares with a
// strict >), D4 subtracts the rate instead of the amount (and ships on the pre-discount total).
public static class Legacy
{
    public static decimal LineTotal(decimal p, int q) => p + q;
    public static decimal DiscountRate(decimal s) { if (s >= 100m) return 0.05m; if (s >= 200m) return 0.10m; return 0m; }
    public static decimal Shipping(decimal discounted, bool isEmpty) => discounted > 100m ? 0m : 15m;
    public static decimal Payable(decimal s, bool isEmpty) => s - DiscountRate(s) + Shipping(s, isEmpty);
}

public sealed record OrderLine(string Name, decimal UnitPrice, int Quantity);
public sealed record Bill(decimal Subtotal, decimal Discount, decimal Discounted, decimal Shipping, decimal Payable);

public delegate decimal DiscountRule(decimal subtotal);   // Task 2 - custom delegate, not Func<>

// Task 2 - pure calculations: no console, no shared state, the caller's list is only read.
public static class Pricing
{
    public static decimal LineTotal(decimal unitPrice, int qty) => unitPrice * qty;
    public static decimal StandardRate(decimal s) => s >= 200m ? 0.10m : s >= 100m ? 0.05m : 0m;
    public static readonly DiscountRule Standard = StandardRate;       // named method
    public static readonly DiscountRule NoDiscount = subtotal => 0m;   // lambda
    public static decimal Subtotal(IReadOnlyList<OrderLine> ls) => ls.Sum(l => LineTotal(l.UnitPrice, l.Quantity));
    public static decimal Shipping(decimal discounted, bool isEmpty) => isEmpty || discounted >= 100m ? 0m : 15m;

    public static Bill Price(IReadOnlyList<OrderLine> ls, DiscountRule rule)
    {
        decimal sub = Subtotal(ls), discount = sub * rule(sub), net = sub - discount;
        decimal ship = Shipping(net, ls.Count == 0);
        return new Bill(sub, discount, net, ship, net + ship);
    }
}

public static class Program
{
    static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    static List<OrderLine> Basket() => new() { new("Notebook", 40m, 2), new("Mouse pad", 30m, 1) };
    static List<OrderLine> Headsets() => new() { new("Headset", 35m, 3) };
    static List<OrderLine> TierTwo() => new() { new("Keyboard", 50m, 2), new("Cable", 25m, 2), new("Stickers", 12.50m, 4) };

    // Task 2 - display is an Action delegate, built once and kept apart from the maths.
    static Action<Bill> Writer(string order, string rule, Action<string> w)
        => b => w($"  {order,-28}{rule,-10}{M(b.Subtotal),10}{M(b.Discount),10}{M(b.Discounted),12}{M(b.Shipping),10}{M(b.Payable),10}");

    // Task 3 - 18 assertions, run against either implementation; [Dn] marks a regression test.
    static bool RunChecks(bool old, Action<string> w)
    {
        int run = 0, bad = 0;
        void Eq(string name, decimal want, decimal got)
        {
            run++;
            if (want == got) { w($"  PASS  {name}"); return; }
            bad++; w($"  FAIL  {name}  ->  expected {M(want)}, actual {M(got)}");
        }
        decimal LT(decimal p, int q) => old ? Legacy.LineTotal(p, q) : Pricing.LineTotal(p, q);
        decimal Rate(decimal s) => old ? Legacy.DiscountRate(s) : Pricing.StandardRate(s);
        decimal Ship(decimal d, bool e) => old ? Legacy.Shipping(d, e) : Pricing.Shipping(d, e);
        decimal Sub(IReadOnlyList<OrderLine> ls) => ls.Sum(l => LT(l.UnitPrice, l.Quantity));
        Bill Price(IReadOnlyList<OrderLine> ls, DiscountRule rule)
        {
            if (!old) return Pricing.Price(ls, rule);
            decimal s = Sub(ls); bool e = ls.Count == 0;
            return new Bill(s, Legacy.DiscountRate(s), s - Legacy.DiscountRate(s), Legacy.Shipping(s, e), Legacy.Payable(s, e));
        }

        w(old ? "\nCHECKS AGAINST THE ORIGINAL CODE" : "\nCHECKS AGAINST THE CORRECTED CODE");
        Eq("01 line total 40.00 x 2 = 80.00              [D1]", 80.00m, LT(40m, 2));
        Eq("02 subtotal 40.00x2 + 30.00x1 = 110.00       [D1]", 110.00m, Sub(Basket()));
        Eq("03 rate(99.99)  = 0.00   below the 5% band", 0m, Rate(99.99m));
        Eq("04 rate(100.00) = 0.05   lower boundary", 0.05m, Rate(100m));
        Eq("05 rate(199.99) = 0.05   still 5%", 0.05m, Rate(199.99m));
        Eq("06 rate(200.00) = 0.10   upper boundary      [D2]", 0.10m, Rate(200m));
        Eq("07 shipping(99.99,  not empty) = 15.00", 15m, Ship(99.99m, false));
        Eq("08 shipping(100.00, not empty) =  0.00       [D3]", 0m, Ship(100m, false));
        Eq("09 shipping(0.00,   empty)     =  0.00       [D3]", 0m, Ship(0m, true));
        Eq("10 110.00 standard: discount = 5.50          [D4]", 5.50m, Price(Basket(), Pricing.Standard).Discount);
        Eq("11 110.00 standard: payable  = 104.50        [D4]", 104.50m, Price(Basket(), Pricing.Standard).Payable);
        Eq("12 110.00 no discount: payable = 110.00", 110.00m, Price(Basket(), Pricing.NoDiscount).Payable);
        Eq("13 105.00 standard: shipping = 15.00         [D4]", 15.00m, Price(Headsets(), Pricing.Standard).Shipping);
        Eq("14 105.00 standard: payable  = 114.75        [D4]", 114.75m, Price(Headsets(), Pricing.Standard).Payable);
        Eq("15 200.00 standard: payable  = 180.00        [D2]", 180.00m, Price(TierTwo(), Pricing.Standard).Payable);
        Eq("16 empty order: payable = 0.00               [D3]", 0.00m, Price(new List<OrderLine>(), Pricing.Standard).Payable);

        List<OrderLine> caller = Basket();                       // input preservation: records compare by value
        Price(caller, Pricing.Standard);
        Eq("17 the caller's collection is unchanged", 1m, caller.SequenceEqual(Basket()) ? 1m : 0m);
        decimal first = Price(Basket(), Pricing.Standard).Payable;
        Price(Basket(), Pricing.NoDiscount);                     // a different rule in between must not leak
        Eq("18 repeated pricing gives the same payable", first, Price(Basket(), Pricing.Standard).Payable);

        w($"  executed {run}, passed {run - bad}, failed {bad}");
        return bad == 0;
    }

    // Task 4 - the required order under both rules, plus the 105.00 order as a second example.
    static void ShowDemo(Action<string> w)
    {
        w($"\nTASK 4 - Standard is the named method '{Pricing.Standard.Method.Name}', " +
          $"NoDiscount is the lambda '{Pricing.NoDiscount.Method.Name}', both of type DiscountRule.");
        w($"  {"order",-28}{"rule",-10}{"subtotal",10}{"discount",10}{"discounted",12}{"shipping",10}{"payable",10}");
        Writer("40.00x2 + 30.00x1", "standard", w)(Pricing.Price(Basket(), Pricing.Standard));
        Writer("40.00x2 + 30.00x1", "none", w)(Pricing.Price(Basket(), Pricing.NoDiscount));
        Writer("35.00x3", "standard", w)(Pricing.Price(Headsets(), Pricing.Standard));
        Writer("35.00x3", "none", w)(Pricing.Price(Headsets(), Pricing.NoDiscount));
        w("  On the 105.00 order the 5% discount of 5.25 drops it to 99.75, back under the");
        w("  free-shipping threshold, so 15.00 shipping returns: 114.75 with the discount,");
        w("  105.00 without it. A discount can leave the customer paying more.");
    }

    public static int Main()
    {
        Action<string> w = Console.WriteLine;
        RunChecks(true, w);                  // evidence: the original code failing
        bool ok = RunChecks(false, w);       // evidence: the corrected code passing
        ShowDemo(w);
        return ok ? 0 : 1;
    }
}
