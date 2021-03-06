using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace ServiceStack.OrmLite.Firebird
{
    public class FirebirdSqlExpression<T> : SqlExpression<T>
    {
        private readonly string _trueExpression;
        private readonly string _falseExpression;

        public FirebirdSqlExpression(IOrmLiteDialectProvider dialectProvider) 
            : base(dialectProvider) 
        {
            _trueExpression = string.Format("({0}={1})", GetQuotedTrueValue(), GetQuotedTrueValue());
            _falseExpression = string.Format("({0}={1})", GetQuotedTrueValue(), GetQuotedFalseValue());
        }

        protected override object VisitBinary(BinaryExpression b)
        {
            object left, right;
            var operand = BindOperant(b.NodeType);   //sep= " " ??
            if (operand == "AND" || operand == "OR")
            {
                var m = b.Left as MemberExpression;
                if (m != null && m.Expression != null
                    && m.Expression.NodeType == ExpressionType.Parameter)
                    left = new PartialSqlString(string.Format("{0}={1}", VisitMemberAccess(m), GetQuotedTrueValue()));
                else
                    left = Visit(b.Left);

                m = b.Right as MemberExpression;
                if (m != null && m.Expression != null
                    && m.Expression.NodeType == ExpressionType.Parameter)
                    right = new PartialSqlString(string.Format("{0}={1}", VisitMemberAccess(m), GetQuotedTrueValue()));
                else
                    right = Visit(b.Right);

                if (left as PartialSqlString == null && right as PartialSqlString == null)
                {
                    var result = Expression.Lambda(b).Compile().DynamicInvoke();
                    return new PartialSqlString(base.DialectProvider.GetQuotedValue(result, result.GetType()));
                }

                if (left as PartialSqlString == null)
                    left = ((bool)left) ? GetTrueExpression() : GetFalseExpression();
                if (right as PartialSqlString == null)
                    right = ((bool)right) ? GetTrueExpression() : GetFalseExpression();
            }
            else
            {
                left = Visit(b.Left);
                right = Visit(b.Right);

                var leftEnum = left as EnumMemberAccess;
                var rightEnum = right as EnumMemberAccess;

                var rightNeedsCoercing = leftEnum != null && rightEnum == null;
                var leftNeedsCoercing = rightEnum != null && leftEnum == null;

                if (rightNeedsCoercing)
                {
                    var rightPartialSql = right as PartialSqlString;
                    if (rightPartialSql == null)
                    {
                        right = GetValue(right, leftEnum.EnumType);
                    }
                }
                else if (leftNeedsCoercing)
                {
                    var leftPartialSql = left as PartialSqlString;
                    if (leftPartialSql == null)
                    {
                        left = DialectProvider.GetQuotedValue(left, rightEnum.EnumType);
                    }
                }
                else if (left as PartialSqlString == null && right as PartialSqlString == null)
                {
                    var result = Expression.Lambda(b).Compile().DynamicInvoke();
                    return result;
                }
                else if (left as PartialSqlString == null)
                {
                    left = DialectProvider.GetQuotedValue(left, left != null ? left.GetType() : null);
                }
                else if (right as PartialSqlString == null)
                {
                    right = GetValue(right, right != null ? right.GetType() : null);
                }
            }

            if (operand == "=" && right.ToString().EqualsIgnoreCase("null"))
                operand = "is";
            else if (operand == "<>" && right.ToString().EqualsIgnoreCase("null"))
                operand = "is not";
            else if (operand == "=" || operand == "<>")
            {
                if (IsTrueExpression(right)) right = GetQuotedTrueValue();
                else if (IsFalseExpression(right)) right = GetQuotedFalseValue();

                if (IsTrueExpression(left)) left = GetQuotedTrueValue();
                else if (IsFalseExpression(left)) left = GetQuotedFalseValue();

            }

            switch (operand)
            {
                case "MOD":
                case "COALESCE":
                    return new PartialSqlString(string.Format("{0}({1},{2})", operand, left, right));
                default:
                    return new PartialSqlString("(" + left + Sep + operand + Sep + right + ")");
            }
        }

        protected override object VisitConstant(ConstantExpression c)
        {
            if (c.Value == null)
                return new PartialSqlString("null");

            if (c.Value is bool)
            {
                object o = base.DialectProvider.GetQuotedValue(c.Value, c.Value.GetType());
                return new PartialSqlString(string.Format("({0}={1})", GetQuotedTrueValue(), o));
            }

            return c.Value;
        }

        protected override object VisitColumnAccessMethod(MethodCallExpression m)
        {
            List<Object> args = this.VisitExpressionList(m.Arguments);
            var quotedColName = Visit(m.Object);
            var statement = "";

            switch (m.Method.Name)
            {
                case "Trim":
                    statement = string.Format("trim({0})", quotedColName);
                    break;
                case "LTrim":
                    statement = string.Format("trim(leading from {0})", quotedColName);
                    break;
                case "RTrim":
                    statement = string.Format("trim(trailing from {0})", quotedColName);
                    break;
                default:
                    return base.VisitColumnAccessMethod(m);
            }
            return new PartialSqlString(statement);
        }

        private bool IsTrueExpression(object exp)
        {
            return (exp.ToString() == _trueExpression);
        }

        private bool IsFalseExpression(object exp)
        {
            return (exp.ToString() == _falseExpression);
        }
    }
}

