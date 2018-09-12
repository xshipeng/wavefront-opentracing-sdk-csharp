﻿using System;
using System.Collections.Generic;
using OpenTracing;
using OpenTracing.Tag;
using static OpenTracing.Mock.MockSpan;

namespace Wavefront.OpenTracing.CSharp.SDK
{
    /// <summary>
    ///     The class used for building <see cref="WavefrontSpan"/> in accordance with the
    ///     OpenTracing spec.
    /// 
    ///     https://github.com/opentracing/specification/blob/master/specification.md
    /// </summary>
    public class WavefrontSpanBuilder : ISpanBuilder
    {
        // The tracer to report spans to.
        private readonly WavefrontTracer tracer;

        // The operation name. Required for every span per OpenTracing spec.
        private readonly string operationName;

        // The list of parent references.
        private IList<Reference> parents;

        // The list of follows from references.
        private IList<Reference> follows;

        private DateTimeOffset? startTimestamp;

        private bool ignoreActiveSpan;

        private readonly List<KeyValuePair<string, string>> tags =
            new List<KeyValuePair<string, string>>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="WavefrontSpanBuilder"/> class.
        /// </summary>
        /// <param name="operationName">The operation name.</param>
        /// <param name="tracer">The tracer.</param>
        public WavefrontSpanBuilder(string operationName, WavefrontTracer tracer)
        {
            this.operationName = operationName;
            this.tracer = tracer;
        }

        /// <inheritdoc />
        public ISpanBuilder AsChildOf(ISpanContext parent)
        {
            return AddReference(References.ChildOf, parent);
        }

        /// <inheritdoc />
        public ISpanBuilder AsChildOf(ISpan parent)
        {
            return AddReference(References.ChildOf, parent?.Context);
        }

        /// <inheritdoc />
        public ISpanBuilder AddReference(string referenceType, ISpanContext referencedContext)
        {
            if (!(referencedContext is WavefrontSpanContext) || 
                (!referenceType.Equals(References.ChildOf) &&
                 !referenceType.Equals(References.FollowsFrom)))
            {
                return this;
            }
            var reference = new Reference((WavefrontSpanContext)referencedContext, referenceType);
            if (referenceType.Equals(References.ChildOf))
            {
                if (parents == null)
                {
                    parents = new List<Reference>(1);
                }
                parents.Add(reference);
            }
            else if (referenceType.Equals(References.FollowsFrom))
            {
                if (follows == null)
                {
                    follows = new List<Reference>(1);
                }
                follows.Add(reference);
            }
            return this;
        }

        /// <inheritdoc />
        public ISpanBuilder IgnoreActiveSpan()
        {
            ignoreActiveSpan = true;
            return this;
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(string key, string value)
        {
            return SetTagObject(key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(string key, bool value)
        {
            return SetTagObject(key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(string key, int value)
        {
            return SetTagObject(key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(string key, double value)
        {
            return SetTagObject(key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(BooleanTag tag, bool value)
        {
            return tag == null ? this : SetTagObject(tag.Key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(IntOrStringTag tag, string value)
        {
            return tag == null ? this : SetTagObject(tag.Key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(IntTag tag, int value)
        {
            return tag == null ? this : SetTagObject(tag.Key, value);
        }

        /// <inheritdoc />
        public ISpanBuilder WithTag(StringTag tag, string value)
        {
            return tag == null ? this : SetTagObject(tag.Key, value);
        }

        private ISpanBuilder SetTagObject(string key, object value)
        {
            if (!string.IsNullOrEmpty(key) && value != null)
            {
                tags.Add(new KeyValuePair<string, string>(key, value.ToString()));
            }
            return this;
        }

        /// <inheritdoc />
        public ISpanBuilder WithStartTimestamp(DateTimeOffset timestamp)
        {
            startTimestamp = timestamp;
            return this;
        }

        /// <inheritdoc />
        public IScope StartActive()
        {
            return StartActive(true);
        }

        /// <inheritdoc />
        public IScope StartActive(bool finishSpanOnDispose)
        {
            return tracer.ScopeManager.Activate(Start(), finishSpanOnDispose);
        }

        /// <inheritdoc />
        public ISpan Start()
        {
            if (!startTimestamp.HasValue)
            {
                startTimestamp = tracer.CurrentTimestamp();
            }
            var globalTags = tracer.Tags;
            if (globalTags != null && globalTags.Count > 0)
            {
                tags.AddRange(globalTags);
            }
            var context = CreateSpanContext();
            return new WavefrontSpan(tracer, operationName, context, startTimestamp.Value, parents,
                                     follows, tags);
        }

        private WavefrontSpanContext CreateSpanContext()
        {
            Guid traceId = TraceAncestry();
            Guid spanId = Guid.NewGuid();
            return new WavefrontSpanContext(traceId, spanId);
        }

        private Guid TraceAncestry()
        {
            if (parents != null && parents.Count > 0)
            {
                // Prefer child_of relationship for assigning traceId.
                return parents[0].SpanContext.GetTraceId();
            }
            if (follows != null && follows.Count > 0)
            {
                return follows[0].SpanContext.GetTraceId();
            }

            // Use active span as parent if ignoreActiveSpan is false.
            var parentSpan = !ignoreActiveSpan ? tracer.ActiveSpan : null;
            if (parentSpan != null)
            {
                AsChildOf(parentSpan);
            }

            // Root span if parentSpan is null
            return parentSpan == null ? Guid.NewGuid() :
                                            ((WavefrontSpanContext)parentSpan.Context).GetTraceId();
        }
    }
}
