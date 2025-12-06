using FluentAssertions;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Tests;

public class DateInfoTests
{
    [Theory]
    [InlineData("15 JAN 1990", 1990, 1, 15, DatePrecision.Day)]
    [InlineData("JAN 1990", 1990, 1, 1, DatePrecision.Month)]
    [InlineData("1990", 1990, 1, 1, DatePrecision.Year)]
    public void Parse_ShouldHandleDifferentPrecisions(string input, int year, int month, int day, DatePrecision precision)
    {
        var result = DateInfo.Parse(input);

        result.Should().NotBeNull();
        result!.Date.Should().Be(new DateOnly(year, month, day));
        result.Precision.Should().Be(precision);
        result.ToString().Should().Contain(year.ToString());
    }

    [Fact]
    public void Parse_ShouldHandleBetweenModifier()
    {
        var result = DateInfo.Parse("BET 1900 AND 1910");

        result.Should().NotBeNull();
        result!.Modifier.Should().Be(DateModifier.Between);
        result.RangeEnd.Should().NotBeNull();
        result.RangeEnd!.Year.Should().Be(1910);
    }

    [Fact]
    public void Parse_ShouldFallbackInvalidDay_ToMonthPrecision()
    {
        var result = DateInfo.Parse("31 FEB 2000");

        result.Should().NotBeNull();
        result!.Precision.Should().Be(DatePrecision.Month);
        result.Month.Should().Be(2);
    }

    [Fact]
    public void ToGeniFormat_ShouldMatchPrecision()
    {
        var yearOnly = new DateInfo { Date = new DateOnly(1995, 1, 1), Precision = DatePrecision.Year };
        var month = new DateInfo { Date = new DateOnly(1995, 2, 1), Precision = DatePrecision.Month };
        var day = new DateInfo { Date = new DateOnly(1995, 2, 3), Precision = DatePrecision.Day };

        yearOnly.ToGeniFormat().Should().Be("1995");
        month.ToGeniFormat().Should().Be("1995-02");
        day.ToGeniFormat().Should().Be("1995-02-03");
    }
}
