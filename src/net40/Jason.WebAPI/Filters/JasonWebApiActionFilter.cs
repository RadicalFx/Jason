﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Topics.Radical.Validation;

namespace Jason.WebAPI.Filters
{
	public class ExecutingActionArgs
	{
		public String CorrelationId { get; set; }
		public Boolean RequestContainsCorrelationId { get; internal set; }

		public Boolean AppendCorrelationIdToResponse { get; set; }

		public bool IsJasonExecute { get; internal set; }

		public Boolean IsCommandInterceptor { get; internal set; }
	}

	public class JasonWebApiActionFilter : IActionFilter
	{
		public Action<ExecutingActionArgs, HttpRequestMessage> OnExecutingAction { get; set; }

		public Action<Object> OnCommandActionIntercepted { get; set; }

		public HttpStatusCode DefaultSuccessfulHttpResponseCode { get; set; }
		
		readonly String correlationIdHeader;

		public JasonWebApiActionFilter( String correlationIdHeader )
		{
			this.correlationIdHeader = correlationIdHeader;
			this.OnExecutingAction = ( cid, request ) => { };
			this.OnCommandActionIntercepted = cmd => { };
		}

		public Task<HttpResponseMessage> ExecuteActionFilterAsync( HttpActionContext actionContext, CancellationToken cancellationToken, Func<Task<HttpResponseMessage>> continuation )
		{
			Ensure.That( actionContext ).Named( () => actionContext ).IsNotNull();
			Ensure.That( continuation ).Named( () => continuation ).IsNotNull();

			var shouldIntercept = actionContext.ActionDescriptor.GetCustomAttributes<InterceptCommandActionAttribute>().SingleOrDefault();
			var isJasonExecute = actionContext.Request.Method == HttpMethod.Post
				&& actionContext.Request.RequestUri.ToString().IndexOf( "api/jason", StringComparison.OrdinalIgnoreCase ) != -1;

			var args = new ExecutingActionArgs();
			args.IsJasonExecute = isJasonExecute;
			args.IsCommandInterceptor = shouldIntercept != null;

			if ( actionContext.Request.Headers.Contains( this.correlationIdHeader ) )
			{
				args.CorrelationId = actionContext.Request.Headers.GetValues( this.correlationIdHeader ).Single();
				args.RequestContainsCorrelationId = true;
				args.AppendCorrelationIdToResponse = true;
			}

			this.OnExecutingAction( args, actionContext.Request );

			if ( shouldIntercept == null )
			{
				return continuation()
					.ContinueWith( t =>
					{
						if ( !t.IsFaulted && args.AppendCorrelationIdToResponse && !String.IsNullOrWhiteSpace( args.CorrelationId ) )
						{
							t.Result.Headers.Add( this.correlationIdHeader, args.CorrelationId );
						}

						return t;
					} )
					.Unwrap();
			}

			return Task.Factory.StartNew( () =>
			{
				var command = actionContext.ActionArguments.Values.OfType<Object>().Single();
				var code = this.DefaultSuccessfulHttpResponseCode;
				if ( shouldIntercept.ResponseCode.HasValue )
				{
					code = shouldIntercept.ResponseCode.Value;
				}

				this.OnCommandActionIntercepted( command );

				var response = new HttpResponseMessage( code );

				return response;
			} );

			
		}

		public bool AllowMultiple
		{
			get { return false; }
		}
	}
}