﻿using System.Diagnostics.Contracts;

namespace NGS.Extensibility
{
	/// <summary>
	/// System aspects will be resolved during system startup.
	/// Services can configure system behaviour.
	/// </summary>
	[ContractClass(typeof(SystemAspectContract))]
	public interface ISystemAspect
	{
		/// <summary>
		/// Initialize aspect and provide system scope
		/// </summary>
		/// <param name="factory">system scope</param>
		void Initialize(IObjectFactory factory);
	}

	[ContractClassFor(typeof(ISystemAspect))]
	internal sealed class SystemAspectContract : ISystemAspect
	{
		public void Initialize(IObjectFactory factory)
		{
			Contract.Requires(factory != null);
		}
	}
}
