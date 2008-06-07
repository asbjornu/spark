using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using MvcContrib.SparkViewEngine.Compiler;
using MvcContrib.UnitTests.SparkViewEngine.Models;
using NUnit.Framework;
using Rhino.Mocks;

namespace MvcContrib.UnitTests.SparkViewEngine
{
	[TestFixture]
	public class ViewCompilerTester
	{
		private MockRepository _mocks;
		private ViewContext _viewContext;
		private HttpContextBase _context;

		[SetUp]
		public void Init()
		{
			_mocks = new MockRepository();
			_context = _mocks.DynamicHttpContextBase();
			var request = _context.Request;
			SetupResult.For(request.ApplicationPath).Return("/");
			SetupResult.For(_context.Response.ApplyAppPathModifier("")).IgnoreArguments().Do(new Func<string, string>(path => path));

			IController controller = _mocks.DynamicMock<IController>();

			RouteData routeData = new RouteData();
			routeData.Values.Add("Controller", "Home");
			routeData.Values.Add("Action", "Index");
			routeData.Values.Add("Id", "");

			_viewContext = new ViewContext(_context, routeData, controller, "index", null, null, null);
		}

		[Test]
		public void MakeAndCompile()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new[] { new SendLiteralChunk { Text = "hello world" } });

			var instance = compiler.CreateInstance();
			string contents = instance.RenderView(_viewContext);

			Assert.That(contents.Contains("hello world"));
		}

		[Test]
		public void UnsafeLiteralCharacters()
		{
			var text = "hello\t\r\n\"world";
			var compiler = new ViewCompiler();
			compiler.CompileView(new[] { new SendLiteralChunk { Text = text } });

			Assert.That(compiler.SourceCode.Contains("Append(\"hello\\t\\r\\n\\\"world\")"));

			var instance = compiler.CreateInstance();
			string contents = instance.RenderView(_viewContext);

			Assert.AreEqual(text, contents);
		}

		[Test]
		public void SimpleOutput()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new[] { new SendExpressionChunk { Code = "3 + 4" } });
			var instance = compiler.CreateInstance();
			string contents = instance.RenderView(_viewContext);
			Assert.AreEqual("7", contents);
		}

		[Test]
		public void LocalVariableDecl()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                                     {
                                          new LocalVariableChunk { Name = "i", Value = "5" }, 
                                          new SendExpressionChunk { Code = "i" }
                                     });
			var instance = compiler.CreateInstance();
			string contents = instance.RenderView(_viewContext);

			Assert.AreEqual("5", contents);
		}

		[Test]
		public void ForEachLoop()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                {
                    new LocalVariableChunk {Name = "data", Value = "new[]{3,4,5}"},
                    new SendLiteralChunk {Text = "<ul>"},
                    new ForEachChunk
                    {
                        Code = "var item in data",
                        Body = new Chunk[]
                        { 
                            new SendLiteralChunk {Text = "<li>"},
                            new SendExpressionChunk {Code = "item"},
                            new SendLiteralChunk {Text = "</li>"}
                        }
                    },
                    new SendLiteralChunk {Text = "</ul>"}
                });
			var instance = compiler.CreateInstance();
			var contents = instance.RenderView(_viewContext);
			Assert.AreEqual("<ul><li>3</li><li>4</li><li>5</li></ul>", contents);
		}

		[Test]
		public void GlobalVariables()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                {
                    new SendExpressionChunk{Code="title"},
                    new AssignVariableChunk{ Name="item", Value="8"},
                    new SendLiteralChunk{ Text=":"},
                    new SendExpressionChunk{Code="item"},
                    new GlobalVariableChunk{ Name="title", Value="\"hello world\""},
                    new GlobalVariableChunk{ Name="item", Value="3"}
                });
			var instance = compiler.CreateInstance();
			var contents = instance.RenderView(_viewContext);
			Assert.AreEqual("hello world:8", contents);
		}



		[Test]
		public void ViewHelpers()
		{
			var viewDataContainer = _mocks.DynamicMock<IViewDataContainer>();

			HtmlHelper html = new HtmlHelper(_viewContext, viewDataContainer);
			RouteTable.Routes.Clear();
			RouteTable.Routes.Add(new Route("{controller}/{action}/{id}", new MvcRouteHandler())
			{
				Defaults = new RouteValueDictionary(new { action = "Index", id = "" })
			});
			_mocks.ReplayAll();

			viewDataContainer.ViewData = _viewContext.ViewData;
			string link = html.ActionLink("Click me", "Reboot");
			Assert.AreEqual("<a href=\"/Home/Reboot\">Click me</a>", link);

			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                {
                    new SendExpressionChunk{Code="Html.ActionLink(\"Click me\", \"Reboot\")"}
                });

			var instance = compiler.CreateInstance();
			var contents = instance.RenderView(_viewContext);
			Assert.AreEqual("<a href=\"/Home/Reboot\">Click me</a>", contents);
		}

		[Test]
		public void DeclareViewData()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                                     {
                                         new ViewDataChunk {Name = "Foo", Type = "string"},
                                         new ViewDataChunk {Name = "Bar", Type = "MvcContrib.UnitTests.SparkViewEngine.Models.Comment"},
                                         new SendExpressionChunk {Code = "Foo"},
                                         new SendExpressionChunk {Code = "Bar.Text"},
                });

			var instance = compiler.CreateInstance();

			var viewContext1 = new ViewContext(
				_context, _viewContext.RouteData, _viewContext.Controller,
				"index", null,
				new ViewDataDictionary(new { Foo = "Hello World", Bar = new Comment { Text = "-yadda-" } }), null);

			var contents = instance.RenderView(viewContext1);
			Assert.AreEqual("Hello World-yadda-", contents);
		}

		[Test]
		public void DeclareViewDataModel()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                                     {
                                         new ViewDataModelChunk { TModel = "MvcContrib.UnitTests.SparkViewEngine.Models.Comment" },
                                         new SendExpressionChunk {Code = "ViewData.Model.Text"},
                });
			var instance = compiler.CreateInstance();

			var viewContext1 = new ViewContext(
				_context, _viewContext.RouteData, _viewContext.Controller,
				"index", null,
				new ViewDataDictionary(new Comment { Text = "MyCommentText" }), null);

			var contents = instance.RenderView(viewContext1);
			Assert.AreEqual("MyCommentText", contents);
		}

		[Test, ExpectedException(typeof(CompilerException))]
		public void ProvideFullException()
		{
			var compiler = new ViewCompiler();
			compiler.CompileView(new Chunk[]
                                     {
                                         new SendExpressionChunk {Code = "NoSuchVariable"}
                });
		}
	}
}