using System.Linq.Expressions;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Joins one predicate per asked-for value into a single <c>OR</c> the store can read, so a list of
/// channels or standings becomes one <c>WHERE</c> rather than a query per value.
/// </summary>
internal static class AnyOf
{
    public static Expression<Func<T, bool>> Matching<T, TAsked>(
        IEnumerable<TAsked> asked,
        Func<TAsked, Expression<Func<T, bool>>> each)
    {
        Expression<Func<T, bool>> nowhere = _ => false;

        return asked.Aggregate(nowhere, (carried, one) => Either(carried, each(one)));
    }

    private static Expression<Func<T, bool>> Either<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        ParameterExpression subject = left.Parameters[0];
        Expression rejoined = new Rebound(right.Parameters[0], subject).Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rejoined), subject);
    }

    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
