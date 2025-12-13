using FluentAssertions;
using GedcomGeniSync.Core.Models.Wave;
using GedcomGeniSync.Core.Services.Wave;
using GedcomGeniSync.Models;

namespace GedcomGeniSync.Tests.Wave;

public class TreeNavigatorTests
{
    [Fact]
    public void GetFamiliesAsSpouse_ShouldReturnEmpty_WhenPersonNotFound()
    {
        // Arrange
        var graph = CreateEmptyGraph();

        // Act
        var families = TreeNavigator.GetFamiliesAsSpouse(graph, "@I999@");

        // Assert
        families.Should().BeEmpty();
    }

    [Fact]
    public void GetFamiliesAsSpouse_ShouldReturnFamilies_WhenPersonIsSpouse()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var families = TreeNavigator.GetFamiliesAsSpouse(graph, "@I1@").ToList();

        // Assert
        families.Should().HaveCount(1);
        families[0].Id.Should().Be("@F1@");
    }

    [Fact]
    public void GetFamiliesAsChild_ShouldReturnEmpty_WhenPersonNotFound()
    {
        // Arrange
        var graph = CreateEmptyGraph();

        // Act
        var families = TreeNavigator.GetFamiliesAsChild(graph, "@I999@");

        // Assert
        families.Should().BeEmpty();
    }

    [Fact]
    public void GetFamiliesAsChild_ShouldReturnFamilies_WhenPersonIsChild()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var families = TreeNavigator.GetFamiliesAsChild(graph, "@I3@").ToList();

        // Assert
        families.Should().HaveCount(1);
        families[0].Id.Should().Be("@F1@");
    }

    [Fact]
    public void GetImmediateRelatives_ShouldReturnSpouse()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var relatives = TreeNavigator.GetImmediateRelatives(graph, "@I1@").ToList();

        // Assert
        relatives.Should().Contain(r => r.personId == "@I2@" && r.relation == RelationType.Spouse);
    }

    [Fact]
    public void GetImmediateRelatives_ShouldReturnChildren()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var relatives = TreeNavigator.GetImmediateRelatives(graph, "@I1@").ToList();

        // Assert
        relatives.Should().Contain(r => r.personId == "@I3@" && r.relation == RelationType.Child);
        relatives.Should().Contain(r => r.personId == "@I4@" && r.relation == RelationType.Child);
    }

    [Fact]
    public void GetImmediateRelatives_ShouldReturnParents()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var relatives = TreeNavigator.GetImmediateRelatives(graph, "@I3@").ToList();

        // Assert
        relatives.Should().Contain(r => r.personId == "@I1@" && r.relation == RelationType.Parent);
        relatives.Should().Contain(r => r.personId == "@I2@" && r.relation == RelationType.Parent);
    }

    [Fact]
    public void GetImmediateRelatives_ShouldReturnSiblings()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var relatives = TreeNavigator.GetImmediateRelatives(graph, "@I3@").ToList();

        // Assert
        relatives.Should().Contain(r => r.personId == "@I4@" && r.relation == RelationType.Sibling);
    }

    [Fact]
    public void GetAllFamilies_ShouldReturnBoth_SpouseAndChildFamilies()
    {
        // Arrange
        var graph = CreateGraphWithTwoGenerations();

        // Act
        var families = TreeNavigator.GetAllFamilies(graph, "@I3@").ToList();

        // Assert
        families.Should().HaveCount(2);
        families.Should().Contain(f => f.family.Id == "@F1@" && f.role == FamilyRole.Child);
        families.Should().Contain(f => f.family.Id == "@F2@" && f.role == FamilyRole.Spouse);
    }

    [Fact]
    public void GetParents_ShouldReturnBothParents()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var parents = TreeNavigator.GetParents(graph, "@I3@").ToList();

        // Assert
        parents.Should().HaveCount(2);
        parents.Should().Contain("@I1@");
        parents.Should().Contain("@I2@");
    }

    [Fact]
    public void GetSpouses_ShouldReturnSpouse()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var spouses = TreeNavigator.GetSpouses(graph, "@I1@").ToList();

        // Assert
        spouses.Should().HaveCount(1);
        spouses.Should().Contain("@I2@");
    }

    [Fact]
    public void GetChildren_ShouldReturnAllChildren()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var children = TreeNavigator.GetChildren(graph, "@I1@").ToList();

        // Assert
        children.Should().HaveCount(2);
        children.Should().Contain("@I3@");
        children.Should().Contain("@I4@");
    }

    [Fact]
    public void GetSiblings_ShouldReturnSiblings_ExcludingSelf()
    {
        // Arrange
        var graph = CreateGraphWithSimpleFamily();

        // Act
        var siblings = TreeNavigator.GetSiblings(graph, "@I3@").ToList();

        // Assert
        siblings.Should().HaveCount(1);
        siblings.Should().Contain("@I4@");
        siblings.Should().NotContain("@I3@");
    }

    [Fact]
    public void GetSiblings_ShouldReturnEmpty_WhenNoSiblings()
    {
        // Arrange
        var graph = CreateGraphWithOnlyChild();

        // Act
        var siblings = TreeNavigator.GetSiblings(graph, "@I3@").ToList();

        // Assert
        siblings.Should().BeEmpty();
    }

    // Helper methods to create test graphs

    private TreeGraph CreateEmptyGraph()
    {
        return new TreeGraph
        {
            PersonsById = new Dictionary<string, PersonRecord>(),
            FamiliesById = new Dictionary<string, FamilyRecord>(),
            PersonToFamiliesAsSpouse = new Dictionary<string, IReadOnlyList<string>>(),
            PersonToFamiliesAsChild = new Dictionary<string, IReadOnlyList<string>>()
        };
    }

    private TreeGraph CreateGraphWithSimpleFamily()
    {
        // Family: @I1@ (John) + @I2@ (Jane) -> @I3@ (Alice), @I4@ (Bob)
        var persons = new Dictionary<string, PersonRecord>
        {
            ["@I1@"] = CreatePerson("@I1@", "John", "Doe"),
            ["@I2@"] = CreatePerson("@I2@", "Jane", "Doe"),
            ["@I3@"] = CreatePerson("@I3@", "Alice", "Doe"),
            ["@I4@"] = CreatePerson("@I4@", "Bob", "Doe")
        };

        var families = new Dictionary<string, FamilyRecord>
        {
            ["@F1@"] = new FamilyRecord
            {
                Id = "@F1@",
                HusbandId = "@I1@",
                WifeId = "@I2@",
                ChildIds = new List<string> { "@I3@", "@I4@" }
            }
        };

        var personToFamiliesAsSpouse = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I1@"] = new List<string> { "@F1@" },
            ["@I2@"] = new List<string> { "@F1@" }
        };

        var personToFamiliesAsChild = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I3@"] = new List<string> { "@F1@" },
            ["@I4@"] = new List<string> { "@F1@" }
        };

        return new TreeGraph
        {
            PersonsById = persons,
            FamiliesById = families,
            PersonToFamiliesAsSpouse = personToFamiliesAsSpouse,
            PersonToFamiliesAsChild = personToFamiliesAsChild
        };
    }

    private TreeGraph CreateGraphWithTwoGenerations()
    {
        // @F1@: @I1@ (John) + @I2@ (Jane) -> @I3@ (Alice)
        // @F2@: @I3@ (Alice) + @I5@ (Tom) -> @I6@ (Charlie)
        var persons = new Dictionary<string, PersonRecord>
        {
            ["@I1@"] = CreatePerson("@I1@", "John", "Doe"),
            ["@I2@"] = CreatePerson("@I2@", "Jane", "Doe"),
            ["@I3@"] = CreatePerson("@I3@", "Alice", "Doe"),
            ["@I5@"] = CreatePerson("@I5@", "Tom", "Smith"),
            ["@I6@"] = CreatePerson("@I6@", "Charlie", "Smith")
        };

        var families = new Dictionary<string, FamilyRecord>
        {
            ["@F1@"] = new FamilyRecord
            {
                Id = "@F1@",
                HusbandId = "@I1@",
                WifeId = "@I2@",
                ChildIds = new List<string> { "@I3@" }
            },
            ["@F2@"] = new FamilyRecord
            {
                Id = "@F2@",
                HusbandId = "@I5@",
                WifeId = "@I3@",
                ChildIds = new List<string> { "@I6@" }
            }
        };

        var personToFamiliesAsSpouse = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I1@"] = new List<string> { "@F1@" },
            ["@I2@"] = new List<string> { "@F1@" },
            ["@I3@"] = new List<string> { "@F2@" },
            ["@I5@"] = new List<string> { "@F2@" }
        };

        var personToFamiliesAsChild = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I3@"] = new List<string> { "@F1@" },
            ["@I6@"] = new List<string> { "@F2@" }
        };

        return new TreeGraph
        {
            PersonsById = persons,
            FamiliesById = families,
            PersonToFamiliesAsSpouse = personToFamiliesAsSpouse,
            PersonToFamiliesAsChild = personToFamiliesAsChild
        };
    }

    private TreeGraph CreateGraphWithOnlyChild()
    {
        // Family: @I1@ (John) + @I2@ (Jane) -> @I3@ (Alice) (only child)
        var persons = new Dictionary<string, PersonRecord>
        {
            ["@I1@"] = CreatePerson("@I1@", "John", "Doe"),
            ["@I2@"] = CreatePerson("@I2@", "Jane", "Doe"),
            ["@I3@"] = CreatePerson("@I3@", "Alice", "Doe")
        };

        var families = new Dictionary<string, FamilyRecord>
        {
            ["@F1@"] = new FamilyRecord
            {
                Id = "@F1@",
                HusbandId = "@I1@",
                WifeId = "@I2@",
                ChildIds = new List<string> { "@I3@" }
            }
        };

        var personToFamiliesAsSpouse = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I1@"] = new List<string> { "@F1@" },
            ["@I2@"] = new List<string> { "@F1@" }
        };

        var personToFamiliesAsChild = new Dictionary<string, IReadOnlyList<string>>
        {
            ["@I3@"] = new List<string> { "@F1@" }
        };

        return new TreeGraph
        {
            PersonsById = persons,
            FamiliesById = families,
            PersonToFamiliesAsSpouse = personToFamiliesAsSpouse,
            PersonToFamiliesAsChild = personToFamiliesAsChild
        };
    }

    private PersonRecord CreatePerson(string id, string firstName, string lastName)
    {
        return new PersonRecord
        {
            Id = id,
            Source = PersonSource.Gedcom,
            FirstName = firstName,
            LastName = lastName
        };
    }
}
