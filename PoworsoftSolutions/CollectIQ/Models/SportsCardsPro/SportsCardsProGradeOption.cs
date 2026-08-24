using System;
using System.Collections.Generic;
using System.Linq;
using CollectIQ.Models;

namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// One SportsCardsPro trading-card condition/grade and the API price key
    /// that represents it.  SportsCardsPro returns all grade prices on the same
    /// product response; the selected option determines which price CollectIQ
    /// displays/stores and which grade text is added to eBay searches.
    /// </summary>
    public sealed class SportsCardsProGradeOption
    {
        public string Label { get; init; } = string.Empty;
        public string ApiPriceKey { get; init; } = string.Empty;
        public string SearchSuffix { get; init; } = string.Empty;
        public string GradingCompany { get; init; } = string.Empty;
        public double? NumericGrade { get; init; }

        public override string ToString() => Label;

        public void ApplyToCard(Card card)
        {
            if (card == null)
                return;

            card.Grading = new Grading
            {
                Company = GradingCompany,
                Grade = NumericGrade
            };
        }
    }

    /// <summary>
    /// SportsCardsPro's documented sports-card grade/condition price keys.
    /// Keep this centralized so Search, Card Detail and Insights use the same list.
    /// </summary>
    public static class SportsCardsProGradeCatalog
    {
        public static IReadOnlyList<SportsCardsProGradeOption> All { get; } = new List<SportsCardsProGradeOption>
        {
            new() { Label = "Ungraded", ApiPriceKey = "loose-price", SearchSuffix = "raw ungraded", GradingCompany = string.Empty, NumericGrade = null },
            new() { Label = "Grade 1", ApiPriceKey = "condition-9-price", SearchSuffix = "graded 1", GradingCompany = "Graded", NumericGrade = 1 },
            new() { Label = "Grade 2", ApiPriceKey = "condition-10-price", SearchSuffix = "graded 2", GradingCompany = "Graded", NumericGrade = 2 },
            new() { Label = "Grade 3", ApiPriceKey = "condition-13-price", SearchSuffix = "graded 3", GradingCompany = "Graded", NumericGrade = 3 },
            new() { Label = "Grade 4", ApiPriceKey = "condition-14-price", SearchSuffix = "graded 4", GradingCompany = "Graded", NumericGrade = 4 },
            new() { Label = "Grade 5", ApiPriceKey = "condition-15-price", SearchSuffix = "graded 5", GradingCompany = "Graded", NumericGrade = 5 },
            new() { Label = "Grade 6", ApiPriceKey = "condition-16-price", SearchSuffix = "graded 6", GradingCompany = "Graded", NumericGrade = 6 },
            new() { Label = "Grade 7 / 7.5", ApiPriceKey = "cib-price", SearchSuffix = "graded 7", GradingCompany = "Graded", NumericGrade = 7 },
            new() { Label = "Grade 8 / 8.5", ApiPriceKey = "new-price", SearchSuffix = "graded 8", GradingCompany = "Graded", NumericGrade = 8 },
            new() { Label = "Grade 9", ApiPriceKey = "graded-price", SearchSuffix = "PSA 9 BGS 9", GradingCompany = "Graded", NumericGrade = 9 },
            new() { Label = "BGS 9.5", ApiPriceKey = "box-only-price", SearchSuffix = "BGS 9.5", GradingCompany = "BGS", NumericGrade = 9.5 },
            new() { Label = "PSA 10", ApiPriceKey = "manual-only-price", SearchSuffix = "PSA 10", GradingCompany = "PSA", NumericGrade = 10 },
            new() { Label = "BGS 10", ApiPriceKey = "bgs-10-price", SearchSuffix = "BGS 10", GradingCompany = "BGS", NumericGrade = 10 },
            new() { Label = "CGC 10", ApiPriceKey = "condition-17-price", SearchSuffix = "CGC 10", GradingCompany = "CGC", NumericGrade = 10 },
            new() { Label = "SGC 10", ApiPriceKey = "condition-18-price", SearchSuffix = "SGC 10", GradingCompany = "SGC", NumericGrade = 10 },
            new() { Label = "CGC 10 Pristine", ApiPriceKey = "condition-19-price", SearchSuffix = "CGC Pristine 10", GradingCompany = "CGC Pristine", NumericGrade = 10 },
            new() { Label = "BGS 10 Black", ApiPriceKey = "condition-20-price", SearchSuffix = "BGS Black Label 10", GradingCompany = "BGS Black Label", NumericGrade = 10 },
            new() { Label = "TAG 10", ApiPriceKey = "condition-21-price", SearchSuffix = "TAG 10", GradingCompany = "TAG", NumericGrade = 10 },
            new() { Label = "ACE 10", ApiPriceKey = "condition-22-price", SearchSuffix = "ACE 10", GradingCompany = "ACE", NumericGrade = 10 }
        };

        public static SportsCardsProGradeOption Ungraded => All[0];

        public static SportsCardsProGradeOption FromCard(Card? card)
        {
            if (card == null)
                return Ungraded;

            Grading grading = card.Grading;
            if (!grading.Grade.HasValue)
                return Ungraded;

            string company = grading.Company?.Trim() ?? string.Empty;
            double grade = grading.Grade.Value;

            SportsCardsProGradeOption? exact = All.FirstOrDefault(option =>
                option.NumericGrade.HasValue &&
                Math.Abs(option.NumericGrade.Value - grade) < 0.01 &&
                !string.IsNullOrWhiteSpace(option.GradingCompany) &&
                string.Equals(option.GradingCompany, company, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            if (Math.Abs(grade - 9.5) < 0.01)
                return All.First(x => x.Label == "BGS 9.5");

            if (Math.Abs(grade - 9) < 0.01)
                return All.First(x => x.Label == "Grade 9");

            int rounded = (int)Math.Floor(grade);
            return All.FirstOrDefault(x => x.Label.StartsWith($"Grade {rounded}", StringComparison.OrdinalIgnoreCase)) ?? Ungraded;
        }
    }
}
