﻿using Jason.Configuration;
using Jason.WebAPI.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Jason.WebAPI
{
	class JasonDelegatingHandler : DelegatingHandler
	{
		readonly String correlationIdHeader;
		readonly IJasonServerConfiguration configuration;
		readonly Func<HttpRequestMessage, Object, HttpResponseMessage> defaultExecutor;

		public JasonDelegatingHandler( String correlationIdHeader, IJasonServerConfiguration configuration, Func<HttpRequestMessage, Object, HttpResponseMessage> defaultExecutor )
		{
			this.correlationIdHeader = correlationIdHeader;
			this.configuration = configuration;
			this.defaultExecutor = defaultExecutor;
		}

		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, System.Threading.CancellationToken cancellationToken )
		{
			var endpoint = this.configuration.GetEndpoint<JasonWebAPIEndpoint>();
			var isJasonExecute = request.Method == HttpMethod.Post
				&& request.RequestUri.AbsolutePath.IndexOf( "api/jason", StringComparison.OrdinalIgnoreCase ) != -1;

			var args = new JasonRequestArgs();
			args.IsJasonExecute = isJasonExecute;
			args.IsCommandInterceptor = false;
			args.HttpRequest = request;

			Task<HttpResponseMessage> task = null;

			if( args.IsJasonExecute )
			{
				if( request.Headers.Contains( this.correlationIdHeader ) )
				{
					args.CorrelationId = request.Headers.GetValues( this.correlationIdHeader ).Single();
					args.AppendCorrelationIdToResponse = true;
				}

				if( endpoint.OnJasonRequest != null )
				{
					endpoint.OnJasonRequest( args );
				}

				var command = Extract( request.Content, typeof( Object ) );
				var result = this.defaultExecutor( args.HttpRequest, command );
				task = Task.FromResult( result );
			}
			else
			{
				task = base.SendAsync( request, cancellationToken );
			}

			task.ContinueWith( t =>
			{
				if( !t.IsFaulted && args.AppendCorrelationIdToResponse && !String.IsNullOrWhiteSpace( args.CorrelationId ) )
				{
					t.Result.Headers.Add( this.correlationIdHeader, args.CorrelationId );
				}
			} );

			return task;
		}

		public static Object Extract( HttpContent content, Type commandType )
		{
			var read = content.ReadAsAsync( commandType );
			read.Wait();

			//reset the internal stream position to allow the WebAPI pipeline to read it again.
			content.ReadAsStreamAsync()
				.ContinueWith( t => t.Result.Seek( 0, SeekOrigin.Begin ) )
				.Wait();

			return read.Result;
		}
	}
}