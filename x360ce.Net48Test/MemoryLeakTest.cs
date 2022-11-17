﻿using System.Runtime;
using System.Reflection;
using System.Diagnostics;
using JocysCom.WebSites.Engine.Security.Data;
using JocysCom.ClassLibrary.Web.Services;
using x360ce.Engine.Data;
using System.Windows.Controls;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Threading;

#if NETCOREAPP
namespace x360ce.Net60Test
#else
namespace x360ce.Net48Test
#endif
{

	/// <summary>
	/// Test if the controls can be disposed and garbage collected and don't result in memory leaks.
	/// IMPORTANT!!!: Enable "Code Optimize" option for memory leak (dispose) test to work:
	///		NET 4.8: Build \ Configuration: Debug \ [x] Optimise Code".
	///		NET 6.0: Build \ General \ Optimize Code: [x] Debug
	/// Note: Compiler is trying to be helpful and Debug build can keep values rooted even if
	/// you set them to null i.e. 'wr.IsAlive' will always return 'true'.
	/// </summary>
	[TestClass]
	public class MemoryLeakTest
	{

		public const long TestMaxDurationPerClassTest = 5000;

		[TestMethod]
		public void Test_x360ce_App() =>
			Test(typeof(App.App).Assembly);

		[TestMethod]
		public void Test_x360ce_App_PadItem_AdvancedControl() =>
			Test<App.Controls.PadItem_AdvancedControl>();

		[TestMethod]
		public void Test_x360ce_Engine() =>
			Test(typeof(Engine.EngineHelper).Assembly,
				// Include types. null = Test all.
				null,
				// Exclude types.
				new Type[] {
					typeof(SecurityEntities),
					typeof(SoapHttpClientBase),
					typeof(x360ceModelContainer),
				});

		[TestMethod]
		public void Test_x360ce_Engine_IssuesUserControl() =>
			Test<JocysCom.ClassLibrary.Controls.IssuesControl.IssuesUserControl>();

		[TestMethod]
		public void Test_x360ce_Engine_IssuesControl() =>
			Test<JocysCom.ClassLibrary.Controls.IssuesControl.IssuesControl>();

		[TestMethod]
		public void Test_ClassLibrary_MessageBoxWindow() =>
			Test<JocysCom.ClassLibrary.Controls.MessageBoxWindow>();

		[TestMethod]
		public void Test_ClassLibrary_ErrorReportControl() =>
			Test<JocysCom.ClassLibrary.Controls.ErrorReportControl>();

		/// <summary>
		/// Simple ListView will be garbage collected successfully.
		/// </summary>
		[TestMethod]
		public void Test_ListView() =>
			Test<System.Windows.Controls.ListView>();

		/// <summary>
		/// Simple DataGrid fails garbage collection and leaks memory.
		/// </summary>
		[TestMethod]
		public void Test_DataGrid() =>
			Test<System.Windows.Controls.DataGrid>();

		[TestMethod]
		public void Test_TextBox() =>
			Test<System.Windows.Controls.TextBox>();

		[TestMethod]
		public void Test_StackPanel() =>
			Test<System.Windows.Controls.StackPanel>();



		#region TestMemoryLeak

#if DEBUG
		static bool isDebug = true;
#else
		static bool isDebug = false;
#endif

		/// <summary>
		/// The main (main) application window never dispose until the application closes.
		/// The secondary window must have the main window as owner in order to be disposed out correctly.
		/// the secondary window will be used for dispose tests.
		/// </summary>
		private static System.Windows.Application MainApp;
		private static System.Windows.Window MainWindow;
		private static System.Windows.Controls.Label MainLabel;
		private static Thread MainThread;
		private static object MainWindowLock = new object();
		private static int TestWindowDisplayDelay = 0;

		private static SemaphoreSlim MainWindowLoadedSemaphore;
		private static SemaphoreSlim ApplicationExitsSemaphore;

		private const int GWL_STYLE = -16;
		private const int WS_SYSMENU = 0x80000;
		[DllImport("user32.dll", SetLastError = true)]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
		[DllImport("user32.dll")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		private static void CheckMainWindow()
		{
			lock (MainWindowLock)
			{
				if (MainWindow != null)
					return;
				MainWindowLoadedSemaphore = new SemaphoreSlim(0);
				ApplicationExitsSemaphore = new SemaphoreSlim(0);

				Action isolator = () =>
				{
					MainApp = new System.Windows.Application();
					var w = new System.Windows.Window();
					w.Title = "Memory Leak Test";
					w.Width = 200;
					w.Topmost = true;
					w.IsHitTestVisible = false;
					w.SizeToContent = SizeToContent.WidthAndHeight;
					// Create content control.
					var sp = new StackPanel();
					MainLabel = new System.Windows.Controls.Label() { Content = w.Title };
					sp.Children.Add(MainLabel);
					w.Content = sp;
					// Use weak reference events.
					EventHandler<RoutedEventArgs> onLoaded = (sender, e) =>
					{
						Console.WriteLine("Owner window loaded");
						MainWindowLoadedSemaphore.Release();
						// Hide title buttons.
						var hwnd = new WindowInteropHelper(MainWindow).Handle;
						SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
					};
					WeakEventManager<Window, RoutedEventArgs>.AddHandler(w, nameof(w.Loaded), onLoaded);
					EventHandler<RoutedEventArgs> onUnloaded = (sender, e) =>
					{
						Console.WriteLine("Owner window unloaded");
					};
					WeakEventManager<Window, RoutedEventArgs>.AddHandler(w, nameof(w.Unloaded), onUnloaded);
					EventHandler<EventArgs> onClosed = (sender, e) =>
					{
						Console.WriteLine("Owner window closed");
					};
					WeakEventManager<Window, EventArgs>.AddHandler(w, nameof(w.Closed), onClosed);
					// Use weak reference events.
					EventHandler<ExitEventArgs> onExit = (sender, e) =>
					{
						Console.WriteLine("Application Exit");
						ApplicationExitsSemaphore.Release();
					};
					WeakEventManager<System.Windows.Application, ExitEventArgs>.AddHandler(MainApp, nameof(MainApp.Exit), onExit);
					MainWindow = w;
					MainApp.Run(MainWindow);
				};
				var ts = new System.Threading.ThreadStart(isolator);
				MainThread = new System.Threading.Thread(ts);
				MainThread.IsBackground = false;
				MainThread.SetApartmentState(ApartmentState.STA);
				MainThread.Start();
				MainWindowLoadedSemaphore.Wait();
			}
		}

		private static void W_Loaded(object sender, RoutedEventArgs e)
		{
			throw new NotImplementedException();
		}

		private static void Test<T>()
		{
			Test(typeof(T).Assembly, new Type[] { typeof(T) });
		}

		public static void Test(Assembly assembly, Type[] includeTypes = null, Type[] excludeTypes = null)
		{
			// Make sure that owner window exists.
			CheckMainWindow();
			var mainWindowWr = new WeakReference(null);
			mainWindowWr.Target = MainWindow;
			var results = TestMemoryLeakByAssembly(assembly, includeTypes, excludeTypes);
			var errors = results.Where(x => x.Level == TraceLevel.Error).ToList();
			var warnings = results.Where(x => x.Level == TraceLevel.Warning).ToList();
			var passed = results.Where(x => x.Level == TraceLevel.Info && !x.IsAlive).ToList();
			var failed = results.Where(x => x.Level == TraceLevel.Info && x.IsAlive).ToList();
			Console.WriteLine();
			Console.WriteLine($"Passed: {passed.Count}");
			Console.WriteLine($"Failed: {failed.Count}");
			Console.WriteLine($"Errors: {errors.Count}");
			Console.WriteLine();
			Console.WriteLine($"Warnings: {warnings.Count}");
			if (results.Count == 1)
				Console.WriteLine($"Duration: {results[0].Duration:#,##0} ms");
			// If more than one control was tested then...
			if (results.Count > 1)
			{
				// Recommend fixing the smallest control next, because
				// more likely that it does not contain other controls, but is used by other controls.
				var nextToFix = failed.OrderBy(x => x.MemObjectSize).FirstOrDefault();
				if (nextToFix != null)
				{
					Console.WriteLine();
					Console.WriteLine($"Smallest recommended control to fix: {nextToFix.Type.FullName}");
				}
			}
			Assert.IsTrue(passed.Count > 0);
			Assert.IsTrue(failed.Count == 0);
			Assert.IsTrue(errors.Count == 0);
			MainApp.Dispatcher.Invoke(() =>
			{
				//MainWindow.Close();
				MainApp.Shutdown();
			});
			// Wait until application exits.
			ApplicationExitsSemaphore.Wait();
			// Make the test completing faster (immediately) by collecting garbage.
			// Wait a bit to allow the application to remove the remaining references.
			Task.Delay(100).Wait();
			CollectGarbage();
		}


		public static List<MemoryTestResult> TestMemoryLeakByAssembly(Assembly assembly, Type[] includeTypes, Type[] excludeTypes)
		{
			var results = new List<MemoryTestResult>();
			// Test public non-abstracts classes only.
			var types = assembly.GetTypes()
				.Where(x => includeTypes == null || includeTypes.Length == 0 || includeTypes.Contains(x))
				.Where(x => excludeTypes == null || excludeTypes.Length == 0 || !excludeTypes.Contains(x))
				.Where(x => x.IsClass)
				.Where(x => x.IsPublic)
				.Where(x => !x.IsAbstract)
				.ToArray();
			var zeros = new string('0', types.Length.ToString().Length);
			var pad = "\r\n" + new string(' ', $"[----] {zeros}/{zeros}: ".Length);
			for (int t = 0; t < types.Length; t++)
			{
				MainApp.Dispatcher.Invoke(() =>
				{
					MainLabel.Content = $"Test control: {t + 1}/{types.Length}";
				});
				var type = types[t];
				var result = TestType(type, includeTypes?.Length == 1);
				results.Add(result);
				var isSuccess = !result.IsAlive && result.Level == TraceLevel.Info;
				// If object was ddisposed without errors then continue
				if (isSuccess)
					continue;
				var status = isSuccess ? "[Pass]" : "[" + result.Level.ToString().Substring(0, 4) + "]";
				var index = string.Format("{0:" + zeros + "}/{1:" + zeros + "}", t + 1, types.Length);
				// Create main message.
				var message = $"{status} {index}: {type.FullName}";
				// Create extra messages lines.
				message += $"{pad}{result.Message}";
				if (result.MemObjectSize.HasValue || result.MemDifference.HasValue)
				{
					message += $"{pad}Object Size: {result.MemObjectSize:#,##0}";
					message += $", Memory Difference: {result.MemDifference:+#,##0;-#,##0;#,##0}";
					message += $", Duration: {result.Duration:#,##0} ms";
				}
				if (isDebug)
					Debug.WriteLine(message);
				else
					Console.WriteLine(message);
			}
			return results;
		}

		public static void CollectGarbage()
		{
			// Try to remove object from the memory.
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
			//GC.Collect();
			GC.WaitForFullGCComplete();
			GC.WaitForPendingFinalizers();
		}

		//[MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
		private static MemoryTestResult TestType(Type type, bool logMoreDetails)
		{
			var stopwatch = new Stopwatch();
			stopwatch.Start();
			var result = new MemoryTestResult();
			result.Type = type;
			var constructor = type.GetConstructor(Type.EmptyTypes);
			if (type.ContainsGenericParameters)
			{
				result.Level = TraceLevel.Warning;
				result.Message += "Requires generic parameters to create instance";
			}
			else if (constructor == null)
			{
				result.Level = TraceLevel.Warning;
				result.Message += "Requires parameters to create instance";
			}
			else
			{
				CollectGarbage();
				try
				{
					var testControlWr = new WeakReference(null);
					var testWindowWr = new WeakReference(null);
					var mainWindowWr = new WeakReference(null);
					long memBeforeCreate = 0;
					Action isolator = () =>
					{
						memBeforeCreate = GC.GetTotalMemory(false);
						var o = Activator.CreateInstance(type);
						result.MemObjectSize = GC.GetTotalMemory(false) - memBeforeCreate;
						testControlWr.Target = o;
						// If WeakReference point to the intended object instance then...
						if (!Equals(testControlWr.Target, o))
						{
							result.Level = TraceLevel.Error;
							result.Message += "Wrong Type!";
						}
						if (o is System.Windows.Window ucw)
						{
							//Console.WriteLine("is Window");
						}
						else if (o is System.Windows.FrameworkElement uc1)
						{
							var testLoadedSemaphore = new SemaphoreSlim(0);
							var testClosedSemaphore = new SemaphoreSlim(0);
							mainWindowWr.Target = MainWindow;
							var testWindow = new System.Windows.Window();
							testWindow.Title = "Test Window";
							testWindow.Topmost = true;
							testWindow.Top = MainWindow.Top + MainWindow.ActualHeight;
							testWindow.Left = MainWindow.Left;
							testWindow.IsHitTestVisible = false;
							testWindow.SizeToContent = SizeToContent.WidthAndHeight;
							// Create content control.
							var sp = new StackPanel() { Orientation = Orientation.Vertical };
							sp.Children.Add(new Label() { Content = "Test Control:" });
							sp.Children.Add(uc1);
							testWindow.Content = sp;
							// Owner must be set to properly expose after closing.
							testWindow.Owner = MainWindow;
							// Control events.
							EventHandler<RoutedEventArgs> onControlLoaded = (sender, e) =>
							{
								if (logMoreDetails)
									Console.WriteLine("    Test control loaded");
							};
							WeakEventManager<FrameworkElement, RoutedEventArgs>.AddHandler(uc1, nameof(uc1.Loaded), onControlLoaded);
							EventHandler<RoutedEventArgs> onControlUnloaded = (sender, e) =>
							{
								if (logMoreDetails)
									Console.WriteLine("    Test control unloaded");
							};
							WeakEventManager<FrameworkElement, RoutedEventArgs>.AddHandler(uc1, nameof(uc1.Loaded), onControlUnloaded);
							// Window events
							EventHandler<RoutedEventArgs> onLoaded = (sender, e) =>
							{
								if (logMoreDetails)
									Console.WriteLine("  Test window loaded");
								testLoadedSemaphore.Release();
							};
							WeakEventManager<Window, RoutedEventArgs>.AddHandler(testWindow, nameof(testWindow.Loaded), onLoaded);
							EventHandler<RoutedEventArgs> onUnloaded = (sender, e) =>
							{
								if (logMoreDetails)
									Console.WriteLine("  Test window unloaded");
							};
							WeakEventManager<Window, RoutedEventArgs>.AddHandler(testWindow, nameof(testWindow.Unloaded), onUnloaded);
							EventHandler<EventArgs> onClosed = (sender, e) =>
							{
								if (logMoreDetails)
									Console.WriteLine("  Test window closed");
								testClosedSemaphore.Release();
							};
							WeakEventManager<Window, EventArgs>.AddHandler(testWindow, nameof(testWindow.Closed), onClosed);
							testWindow.Show();
							testLoadedSemaphore.Wait();
							Task.Delay(TestWindowDisplayDelay).Wait();
							testWindow.Close();
							testClosedSemaphore.Wait();
						}
						else if (o is System.Windows.Forms.UserControl uc2)
						{
							uc2.Dispose();
							uc2 = null;
						}
						else if (o is IDisposable uc3)
						{
							uc3.Dispose();
							uc3 = null;
						}

						if (o is DataGrid dg)
						{

						}
						// Trigger object dispose.
						o = null;
					};
					// Create new window on the same thread.
					MainWindow.Dispatcher.Invoke(isolator);
					// loop untill object allive, but no longer than  seconds.
					while (testControlWr.IsAlive && stopwatch.ElapsedMilliseconds < TestMaxDurationPerClassTest)
					{
						Task.Delay(200).Wait();
						CollectGarbage();
					}
					result.MemDifference = GC.GetTotalMemory(true) - memBeforeCreate;
					result.IsAlive = testControlWr.IsAlive;
					// if dispose failed. Strong references are left to the wr.Target.
					result.Message += testControlWr.IsAlive
						? "Dispose Failed!"
						: "Disposed";
					if (logMoreDetails)
					{
						Console.WriteLine($"Main Window  IsAlive: {mainWindowWr.IsAlive}");
						Console.WriteLine($"Test Window  IsAlive: {testWindowWr.IsAlive}");
						Console.WriteLine($"Test Control IsAlive: {testControlWr.IsAlive}");
					}
				}
				catch (Exception ex)
				{
					if (isDebug)
						throw;
					result.Level = TraceLevel.Error;
					result.Message = ex.Message;
					result.Exception = ex;
					Console.WriteLine(ex.ToString());
				}
			}
			result.Duration = stopwatch.ElapsedMilliseconds;
			return result;
		}

		#endregion

	}
}