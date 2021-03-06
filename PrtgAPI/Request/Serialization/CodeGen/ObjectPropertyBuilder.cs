﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Serialization;
using PrtgAPI.Attributes;
using PrtgAPI.Parameters.Helpers;
using PrtgAPI.Reflection.Cache;
using PrtgAPI.Request.Serialization;
using PrtgAPI.Utilities;
using XmlMapping = PrtgAPI.Request.Serialization.XmlMapping;

namespace PrtgAPI.Linq.Expressions.Serialization
{
    class ObjectPropertyBuilder
    {
        private Dictionary<Type, List<PropertyCache>> propertyMap = new Dictionary<Type, List<PropertyCache>>();

        public ObjectPropertyBuilder()
        {
        }

        public LambdaExpression BuildDeserializer()
        {
            var property = Expression.Parameter(typeof(ObjectProperty), "property");
            var rawValue = Expression.Parameter(typeof(string), "rawValue");

            var result = Expression.Variable(typeof(object), "result");

            var internalLambda = MakeSwitchLambda(property, rawValue);

            var assignResult = result.Assign(Expression.Invoke(internalLambda, XmlExpressionConstants.Serializer, property, rawValue));

            var test = result.Equal(Expression.Constant(null)).AndAlso(rawValue.NotEqual(Expression.Constant(string.Empty)));

            var condition = Expression.Condition(test, Expression.Convert(rawValue, typeof(object)), result);

            var block = Expression.Block(
                new[] { result },
                assignResult,
                condition
            );

            return Expression.Lambda(
                block,
                "ReadObjectPropertyOuter",
                new[] { XmlExpressionConstants.Serializer, property, rawValue }
            );
        }

        private LambdaExpression MakeSwitchLambda(ParameterExpression property, ParameterExpression rawValue)
        {
            var c = ReflectionCacheManager.GetEnumCache(typeof(ObjectProperty)).Cache;

            var fields = c.Fields;

            List<SwitchCase> cases = new List<SwitchCase>();

            foreach (var f in fields)
            {
                if (f.Field.FieldType.IsEnum)
                {
                    var val = f.Field.GetValue(null);

                    Expression body = GetCaseBody((Enum)val, rawValue);

                    if (body != null)
                    {
                        if(body.NodeType != ExpressionType.Block)
                            body = Expression.Convert(body, typeof(object));

                        cases.Add(Expression.SwitchCase(body, Expression.Constant(val)));
                    }
                }
            }

            var @default = Expression.Constant(null);

            var assignName = XmlExpressionConstants.Serializer_Name(XmlAttributeType.Element).Assign(property.Call("ToString").Call("ToLower"));

            var @switch = Expression.Switch(property, @default, cases.ToArray());

            return Expression.Lambda(
                Expression.Block(
                    assignName,
                    @switch
                ),
                "ReadObjectPropertyInner",
                new[] { XmlExpressionConstants.Serializer, property, rawValue });
        }

        private Expression GetCaseBody(Enum property, Expression rawValue)
        {
            var viaObject = false;

            var typeLookup = property.GetEnumAttribute<TypeLookupAttribute>().Class;

            var mappings = ReflectionCacheManager.Map(typeLookup).Cache;
            var cache = ObjectPropertyParser.GetPropertyInfoViaTypeLookup(property);
            var xmlElement = cache.GetAttribute<XmlElementAttribute>(); //todo: what if this objectproperty doesnt point to a member with an xmlelementattribute?

            XmlMapping mapping = null;

            if(xmlElement != null)
            {
                mapping = mappings.FirstOrDefault(m => m.AttributeValue[0] == xmlElement.ElementName);
            }
            else
            {
                //We have a property like Interval which uses a backing property instead.
                //Get the backing property so that we may extract the real value from the public
                //property
                var rawName = ObjectPropertyParser.GetObjectPropertyNameViaCache(property, cache);
                var elementName = $"{ObjectSettings.prefix}{rawName.TrimEnd('_')}";

                mapping = mappings.FirstOrDefault(m => m.AttributeValue[0] == elementName);

                if (mapping != null)
                    viaObject = true;
            }

            if(mapping != null)
            {
                var deserializer = new ValueDeserializer(mapping, null, rawValue);
                var result = deserializer.Deserialize();

                if(viaObject)
                {
                    //Construct an expression like return new TableSettings { intervalStr = "60|60 seconds" }.Interval;
                    var settingsObj = Expression.Variable(typeLookup, "obj");
                    var assignObj = settingsObj.Assign(Expression.New(typeLookup));
                    var internalProp = Expression.MakeMemberAccess(settingsObj, mapping.PropertyCache.Property);
                    var assignInternal = internalProp.Assign(result);
                    var externalProp = Expression.MakeMemberAccess(settingsObj, cache.Property);

                    return Expression.Block(
                        new[] {settingsObj},
                        assignObj,
                        assignInternal,
                        Expression.Convert(externalProp, typeof(object))
                    );
                }

                return result;
            }

            //Property is not deserializable
            return null;
        }
    }
}
