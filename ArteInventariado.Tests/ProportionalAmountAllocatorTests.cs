using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public class ProportionalAmountAllocatorTests
{
    [Fact]
    public void Allocate_PreservesEveryCentWithDeterministicRemainderOrder()
    {
        var allocation = ProportionalAmountAllocator.Allocate(7, [1, 1, 1]);

        Assert.Equal([3L, 2L, 2L], allocation);
        Assert.Equal(7, allocation.Sum());
    }

    [Fact]
    public void Allocate_UsesLargestRemainderBeforeOriginalLineOrder()
    {
        var allocation = ProportionalAmountAllocator.Allocate(11, [100, 200, 300]);

        Assert.Equal([2L, 4L, 5L], allocation);
        Assert.Equal(11, allocation.Sum());
    }

    [Fact]
    public void Allocate_DoesNotOverflowWithLargeCentValues()
    {
        var allocation = ProportionalAmountAllocator.Allocate(
            8_000_000_000_000_000_000,
            [4_000_000_000_000_000_000, 2_000_000_000_000_000_000]);

        Assert.Equal(8_000_000_000_000_000_000, allocation.Sum());
        Assert.True(allocation[0] > allocation[1]);
    }

    [Fact]
    public void Allocate_RejectsPositiveAmountWithoutAnyPositiveBase()
    {
        Assert.Throws<ArgumentException>(() => ProportionalAmountAllocator.Allocate(1, [0, 0]));
    }
}
