using System.Linq.Expressions;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ProgrammeSearchRepository(CarinaDbContext context) : IProgrammeSearchRepository
{
    public async Task<PaginatedList<ProgrammeMatch>> SearchAsync(
        ProgrammeSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        IQueryable<ProgrammeMatch> found = context.Set<ProgrammeMatch>()
            .Where(match => !match.IsShadow);

        foreach (string word in search.Words)
        {
            found = Carrying(found, word, search.Fields);
        }

        foreach (string word in search.ExcludedWords)
        {
            found = Without(found, word, search.Fields);
        }

        if (search.Genres.Count > 0)
        {
            int[] asked = [.. search.Genres];

            found = found.Where(match =>
                EF.Property<int[]>(match, ProgrammeConfiguration.GenreKinds).Any(kind => asked.Contains(kind)));
        }

        if (search.Channels.Count > 0)
        {
            found = OnAnyOf(found, search.Channels);
        }

        if (search.Services is { } within)
        {
            found = OnAnyOf(found, within);
        }

        if (search.Withheld.Count > 0)
        {
            found = OnNoneOf(found, search.Withheld);
        }

        if (search.From is { } from)
        {
            found = found.Where(match => match.EndsAt == null || match.EndsAt > from);
        }

        if (search.To is { } to)
        {
            found = found.Where(match => match.StartsAt < to);
        }

        int total = await found.CountAsync(cancellationToken);
        IOrderedQueryable<ProgrammeMatch> ordered = (search.Sort, search.Descending) switch
        {
            (ProgrammeSort.Name, false) => found.OrderBy(match => match.Name),
            (ProgrammeSort.Name, true) => found.OrderByDescending(match => match.Name),
            (_, true) => found.OrderByDescending(match => match.StartsAt),
            _ => found.OrderBy(match => match.StartsAt),
        };
        List<ProgrammeMatch> page = await ordered
            .ThenBy(match => match.EventId)
            .Skip((search.Page - 1) * search.PerPage)
            .Take(search.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<ProgrammeMatch>(page, total, search.Page, search.PerPage);
    }

    private static IQueryable<ProgrammeMatch> Carrying(
        IQueryable<ProgrammeMatch> found,
        string word,
        IReadOnlyList<ProgrammeField> fields)
    {
        IQueryable<ProgrammeMatch> narrowed = found.Where(match => EF.Functions.Like(
            EF.Property<string>(match, ProgrammeConfiguration.Searchable),
            "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%"));

        return (fields.Contains(ProgrammeField.Title), fields.Contains(ProgrammeField.Description)) switch
        {
            (true, false) => narrowed.Where(match => EF.Functions.Like(
                BroadcastText.Normalised(match.Name, BroadcastText.Compatibility).ToLower(),
                "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%")),
            (false, true) => narrowed.Where(match => EF.Functions.Like(
                BroadcastText.Normalised(match.Summary, BroadcastText.Compatibility).ToLower(),
                "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%")),
            _ => narrowed,
        };
    }

    private static IQueryable<ProgrammeMatch> Without(
        IQueryable<ProgrammeMatch> found,
        string word,
        IReadOnlyList<ProgrammeField> fields)
        => (fields.Contains(ProgrammeField.Title), fields.Contains(ProgrammeField.Description)) switch
        {
            (true, false) => found.Where(match => !EF.Functions.Like(
                BroadcastText.Normalised(match.Name, BroadcastText.Compatibility).ToLower(),
                "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%")),
            (false, true) => found.Where(match => !EF.Functions.Like(
                BroadcastText.Normalised(match.Summary, BroadcastText.Compatibility).ToLower(),
                "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%")),
            _ => found.Where(match => !EF.Functions.Like(
                EF.Property<string>(match, ProgrammeConfiguration.Searchable),
                "%" + BroadcastText.Normalised(word, BroadcastText.Compatibility).ToLower() + "%")),
        };

    private static IQueryable<ProgrammeMatch> OnAnyOf(
        IQueryable<ProgrammeMatch> found,
        IReadOnlyList<ProgrammeService> services)
    {
        Expression<Func<ProgrammeMatch, bool>> nowhere = match => false;

        return found.Where(services.Aggregate(nowhere, (carried, service) => Either(carried, On(service))));
    }

    private static IQueryable<ProgrammeMatch> OnNoneOf(
        IQueryable<ProgrammeMatch> found,
        IReadOnlyList<ProgrammeService> services)
    {
        Expression<Func<ProgrammeMatch, bool>> nowhere = match => false;
        Expression<Func<ProgrammeMatch, bool>> anywhere =
            services.Aggregate(nowhere, (carried, service) => Either(carried, On(service)));

        return found.Where(Expression.Lambda<Func<ProgrammeMatch, bool>>(
            Expression.Not(anywhere.Body),
            anywhere.Parameters[0]));
    }

    private static Expression<Func<ProgrammeMatch, bool>> On(ProgrammeService service)
    {
        var network = new NetworkId(service.NetworkId);
        var carried = new ServiceId(service.ServiceId);

        return match => match.NetworkId == network && match.ServiceId == carried;
    }

    private static Expression<Func<ProgrammeMatch, bool>> Either(
        Expression<Func<ProgrammeMatch, bool>> left,
        Expression<Func<ProgrammeMatch, bool>> right)
    {
        ParameterExpression match = left.Parameters[0];
        Expression rejoined = new Rebound(right.Parameters[0], match).Visit(right.Body);

        return Expression.Lambda<Func<ProgrammeMatch, bool>>(
            Expression.OrElse(left.Body, rejoined),
            match);
    }

    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
