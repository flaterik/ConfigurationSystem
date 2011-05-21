using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Security;

namespace MySpace.Common.HelperObjects
{
	/// <summary>
	/// This is a utility class for getting readable stack dumps.
	/// </summary>
	public class StackDescriber
	{
		/// <summary>
		/// Get a current stack trace and create a desciption of it with the same format as the StackTrace object. If excludeSystem is specified the stack will stop at 
		/// the first frame that is missing a file name. 
		/// </summary>		
		public static string DescribeCurrentStack(bool excludeSystem)
		{
			StringBuilder usefulStack = new StringBuilder();
			try
			{
				var stack = new StackTrace(2, true);
				var frames = stack.GetFrames();
				bool lastOne = false;
				for (int i = 0; i < frames.Length && !lastOne; i++)
				{
					if (excludeSystem && frames[i].GetFileName() == null)
						lastOne = true;						

					DescribeStackFrame(usefulStack, frames[i]);
				}
			}
			catch (Exception e)
			{
				return string.Format("Exception generating strack description: {0}", e);
			}
			
			return usefulStack.ToString();
		}

		/// <summary>
		/// Describe a stack frame with the same format as StackTrace.ToString()
		/// </summary>		
		public static string DescribeStackFrame(StackFrame frame) 
		{
			StringBuilder builder = new StringBuilder();
			DescribeStackFrame(builder, frame);
			return builder.ToString();
		}

		/// <summary>
		/// Append the description of a single stack frame to a string builder  with the same format as StackTrace.ToString() 
		/// </summary>		
		public static string DescribeStackFrame(StringBuilder builder, StackFrame frame) //stolen from StackTrace.ToString, which is oddly different than frame.ToString
		{ 
			string resourceString = "at";
			string format = "in {0}:line {1}";

			bool flag = true;

			//	StackFrame frame = this.GetFrame(i);
			MethodBase method = frame.GetMethod();
			if (method != null)
			{
				if (flag)
				{
					flag = false;
				}
				else
				{
					builder.Append(Environment.NewLine);
				}
				builder.AppendFormat("   {0} ", new object[] { resourceString });
				Type declaringType = method.DeclaringType;
				if (declaringType != null)
				{
					builder.Append(declaringType.FullName.Replace('+', '.'));
					builder.Append(".");
				}
				builder.Append(method.Name);
				if ((method is MethodInfo) && ((MethodInfo)method).IsGenericMethod)
				{
					Type[] genericArguments = ((MethodInfo)method).GetGenericArguments();
					builder.Append("[");
					int index = 0;
					bool flag2 = true;
					while (index < genericArguments.Length)
					{
						if (!flag2)
						{
							builder.Append(",");
						}
						else
						{
							flag2 = false;
						}
						builder.Append(genericArguments[index].Name);
						index++;
					}
					builder.Append("]");
				}
				builder.Append("(");
				ParameterInfo[] parameters = method.GetParameters();
				bool flag3 = true;
				for (int j = 0; j < parameters.Length; j++)
				{
					if (!flag3)
					{
						builder.Append(", ");
					}
					else
					{
						flag3 = false;
					}
					string name = "<UnknownType>";
					if (parameters[j].ParameterType != null)
					{
						name = parameters[j].ParameterType.Name;
					}
					builder.Append(name + " " + parameters[j].Name);
				}
				builder.Append(")");
				if (frame.GetILOffset() != -1)
				{
					string fileName = null;
					try
					{
						fileName = frame.GetFileName();
					}
					catch (SecurityException)
					{
					}
					if (fileName != null)
					{
						builder.Append(' ');
						builder.AppendFormat(format, new object[] { fileName, frame.GetFileLineNumber() });
					}
				}
			}

			builder.Append(Environment.NewLine);

			return builder.ToString();
		}

	}
}
