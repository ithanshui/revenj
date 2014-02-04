﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using NGS;
using NGS.Common;
using NGS.DomainPatterns;
using NGS.Extensibility;
using NGS.Security;
using NGS.Serialization;
using NGS.Utility;
using Revenj.Processing;

namespace Revenj.Plugins.Server.Commands
{
	[Export(typeof(IServerCommand))]
	[ExportMetadata(Metadata.ClassType, typeof(PersistAggregateRoot))]
	public class PersistAggregateRoot : IServerCommand
	{
		private readonly IServiceLocator Locator;
		private readonly IDomainModel DomainModel;
		private readonly IPermissionManager Permissions;

		public PersistAggregateRoot(
			IServiceLocator locator,
			IDomainModel domainModel,
			IPermissionManager permissions)
		{
			Contract.Requires(locator != null);
			Contract.Requires(domainModel != null);
			Contract.Requires(permissions != null);

			this.Locator = locator;
			this.DomainModel = domainModel;
			this.Permissions = permissions;
		}

		[DataContract(Namespace = "")]
		public class Argument<TFormat>
		{
			[DataMember]
			public string RootName;
			[DataMember]
			public TFormat ToInsert;
			[DataMember]
			public TFormat ToUpdate;
			[DataMember]
			public TFormat ToDelete;
		}

		private static TFormat CreateExampleArgument<TFormat>(ISerialization<TFormat> serializer)
		{
			return serializer.Serialize(new Argument<TFormat> { RootName = "Module.AggregateRoot" });
		}

		private static TFormat CreateExampleArgument<TFormat>(ISerialization<TFormat> serializer, Type rootType)
		{
			try
			{
				var array = Array.CreateInstance(rootType, 1);
				var element = TemporaryResources.CreateRandomObject(rootType);
				array.SetValue(element, 0);
				return serializer.Serialize(new Argument<TFormat> { RootName = rootType.FullName, ToInsert = serializer.Serialize((dynamic)array) });
			}
			catch
			{
				//fallback to simple example since sometimes calculated properties will throw exception during serialization
				return CreateExampleArgument(serializer);
			}
		}

		public ICommandResult<TOutput> Execute<TInput, TOutput>(ISerialization<TInput> input, ISerialization<TOutput> output, TInput data)
		{
			var either = CommandResult<TOutput>.Check<Argument<TInput>, TInput>(input, output, data, CreateExampleArgument);
			if (either.Error != null)
				return either.Error;
			var argument = either.Argument;

			var rootType = DomainModel.Find(argument.RootName);
			if (rootType == null)
				return CommandResult<TOutput>.Fail("Couldn't find root type {0}.".With(argument.RootName), @"Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output)));

			if (!typeof(IAggregateRoot).IsAssignableFrom(rootType))
				return CommandResult<TOutput>.Fail(@"Specified type ({0}) is not an aggregate root. 
Please check your arguments.".With(argument.RootName), null);

			if (!Permissions.CanAccess(rootType))
				return
					CommandResult<TOutput>.Return(
						HttpStatusCode.Forbidden,
						default(TOutput),
						"You don't have permission to access: {0}.",
						argument.RootName);

			if (argument.ToInsert == null && argument.ToUpdate == null && argument.ToDelete == null)
				return CommandResult<TOutput>.Fail("Data to persist not specified.", @"Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output, rootType)));

			try
			{
				var commandType = typeof(PersistAggregateRootCommand<>).MakeGenericType(rootType);
				var command = Activator.CreateInstance(commandType) as IPersistAggregateRootCommand<IAggregateRoot>;
				var uris = command.Persist(input, Locator, argument.ToInsert, argument.ToUpdate, argument.ToDelete);

				return CommandResult<TOutput>.Success(output.Serialize(uris), "Data persisted");
			}
			catch (ArgumentException ex)
			{
				return CommandResult<TOutput>.Fail(
					ex.Message,
					ex.GetDetailedExplanation() + @"
Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output)));
			}
		}

		private interface IPersistAggregateRootCommand<out TRoot>
			where TRoot : IAggregateRoot
		{
			string[] Persist<TFormat>(
				ISerialization<TFormat> serializer,
				IServiceLocator locator,
				TFormat toInsert,
				TFormat toUpdate,
				TFormat toDelete);
		}

		private class PersistAggregateRootCommand<TRoot> : IPersistAggregateRootCommand<TRoot>
			where TRoot : IAggregateRoot, new()
		{
			public string[] Persist<TFormat>(
				ISerialization<TFormat> serializer,
				IServiceLocator locator,
				TFormat toInsert,
				TFormat toUpdate,
				TFormat toDelete)
			{
				var repository = locator.Resolve<IPersistableRepository<TRoot>>();
				var insertData = toInsert != null ? serializer.Deserialize<TFormat, TRoot[]>(toInsert, locator) : null;
				var updateData = toUpdate != null ? serializer.Deserialize<TFormat, KeyValuePair<TRoot, TRoot>[]>(toUpdate, locator) : null;
				//TODO support old update format
				if (toUpdate != null && updateData != null && updateData.Length == 0)
				{
					var updateValues = serializer.Deserialize<TFormat, TRoot[]>(toUpdate, locator);
					if (updateValues != null && updateValues.Length > 0)
						updateData = updateValues.Select(it => new KeyValuePair<TRoot, TRoot>(default(TRoot), it)).ToArray();
				}
				var deleteData = toDelete != null ? serializer.Deserialize<TFormat, TRoot[]>(toDelete, locator) : null;

				if ((insertData == null || insertData.Length == 0)
					&& (updateData == null || updateData.Length == 0)
					&& (deleteData == null || deleteData.Length == 0))
					throw new ArgumentException(
						"Data not sent or deserialized unsuccessfully.",
						new FrameworkException(@"Example:
" + serializer.Serialize(
			new Argument<TFormat>
			{
				RootName = typeof(TRoot).FullName,
				ToInsert = serializer.Serialize(new TRoot[] { new TRoot() })
			})));
				try
				{
					return repository.Persist(insertData, updateData, deleteData);
				}
				catch (FrameworkException ex)
				{
					throw new ArgumentException(ex.Message, ex);
				}
				catch (Exception ex)
				{
					throw new ArgumentException(
						"Error persisting: {0}.".With(ex.Message),
						new FrameworkException(
							@"{0}{1}{2}".With(
								FormatData(serializer, "Insert", insertData),
								FormatData(serializer, "Update", updateData),
								FormatData(serializer, "Delete", deleteData)),
							ex));
				}
			}

			private static string FormatData<T, TFormat>(ISerialization<TFormat> serializer, string text, T[] data)
			{
				return data != null && data.Length > 0
					? "{0}{1} (first two): {2}".With(Environment.NewLine, text, serializer.Serialize(data.Take(2).ToArray()))
					: string.Empty;
			}
		}
	}

	public static class PersistAggregateRootHelper
	{
		internal class Repository<TData, TFormat> : IPersistableRepository<TData>
		{
			private readonly IServiceLocator Locator;
			private readonly GetDomainObject GetCommand;
			private readonly SearchDomainObject SearchCommand;
			private readonly PersistAggregateRoot PersistCommand;
			private readonly ISerialization<TFormat> Serializer;

			internal Repository(
				IServiceLocator locator,
				GetDomainObject getCommand,
				SearchDomainObject searchCommand,
				PersistAggregateRoot persistCommand,
				ISerialization<TFormat> serializer)
			{
				this.Locator = locator;
				this.GetCommand = getCommand;
				this.SearchCommand = searchCommand;
				this.PersistCommand = persistCommand;
				this.Serializer = serializer;
			}

			public TData[] Find(IEnumerable<string> uris)
			{
				return
					GetDomainObjectHelper.Repository<TData, TFormat>.Find(
						Locator,
						GetCommand,
						Serializer,
						uris);
			}

			public IQueryable<TData> Query<TCondition>(ISpecification<TCondition> specification)
			{
				return
					SearchDomainObjectHelper.Repository<TData, TFormat>.Query(
						Locator,
						SearchCommand,
						Serializer,
						specification);
			}

			public TData[] Search<TCondition>(ISpecification<TCondition> specification, int? limit, int? offset)
			{
				return
					SearchDomainObjectHelper.Repository<TData, TFormat>.Search(
						Locator,
						SearchCommand,
						Serializer,
						specification,
						limit,
						offset);
			}

			public string[] Persist(IEnumerable<TData> insert, IEnumerable<KeyValuePair<TData, TData>> update, IEnumerable<TData> delete)
			{
				var result =
					PersistCommand.Execute(
						Serializer,
						Serializer,
						Serializer.Serialize(
							new PersistAggregateRoot.Argument<TFormat>
							{
								RootName = typeof(TData).FullName,
								ToInsert = insert != null ? Serializer.Serialize(insert.ToArray()) : default(TFormat),
								ToUpdate = update != null ? Serializer.Serialize(update.Select(it => it.Value).ToArray()) : default(TFormat),
								ToDelete = delete != null ? Serializer.Serialize(delete.ToArray()) : default(TFormat)
							}));
				return Serializer.Deserialize<TFormat, string[]>(result.Data);
			}
		}

		public static IPersistableRepository<TData> CreatePersistableRepository<TData, TFormat>(this IServiceLocator locator)
		{
			return
				new Repository<TData, TFormat>(
					locator,
					locator.Resolve<GetDomainObject>(),
					locator.Resolve<SearchDomainObject>(),
					locator.Resolve<PersistAggregateRoot>(),
					locator.Resolve<ISerialization<TFormat>>());
		}
	}
}
