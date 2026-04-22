using FluentAssertions;
using Flexischools.Api.Domain.Entities;
using NUnit.Framework;

namespace Flexischools.Tests.Unit.Domain;

[TestFixture]
public class CanteenTests
{
    private Canteen _canteen = null!;

    [SetUp]
    public void SetUp()
    {
        _canteen = Canteen.Create(
            "Greenfield Canteen",
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            new TimeSpan(9, 30, 0));
    }

    [Test]
    [TestCase(9, 0, DayOfWeek.Monday, true)]
    [TestCase(9, 29, DayOfWeek.Monday, true)]
    [TestCase(9, 30, DayOfWeek.Monday, false)]  // At cut-off — not strictly before
    [TestCase(10, 0, DayOfWeek.Monday, false)]  // After cut-off
    public void IsOrderAllowed_CutOffBoundaryBehaviour(int hour, int minute, DayOfWeek day, bool expected)
    {
        // Find the next occurrence of the specified day
        var date = DateTime.Today;
        while (date.DayOfWeek != day) date = date.AddDays(1);

        var fulfilmentDate = DateOnly.FromDateTime(date);
        var currentTime = new TimeOnly(hour, minute);

        _canteen.IsOrderAllowed(fulfilmentDate, currentTime).Should().Be(expected);
    }

    [Test]
    public void IsOrderAllowed_ClosedDay_ReturnsFalse()
    {
        // Tuesday is not in the open days
        var tuesday = DateTime.Today;
        while (tuesday.DayOfWeek != DayOfWeek.Tuesday) tuesday = tuesday.AddDays(1);

        _canteen.IsOrderAllowed(DateOnly.FromDateTime(tuesday), new TimeOnly(8, 0))
            .Should().BeFalse();
    }

    [Test]
    public void IsOrderAllowed_OpenDay_BeforeCutOff_ReturnsTrue()
    {
        var wednesday = DateTime.Today;
        while (wednesday.DayOfWeek != DayOfWeek.Wednesday) wednesday = wednesday.AddDays(1);

        _canteen.IsOrderAllowed(DateOnly.FromDateTime(wednesday), new TimeOnly(8, 0))
            .Should().BeTrue();
    }
}
