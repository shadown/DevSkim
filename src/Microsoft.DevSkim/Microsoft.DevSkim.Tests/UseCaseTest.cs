﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Microsoft.DevSkim.Tests
{
    [TestClass]
    [ExcludeFromCodeCoverage]
    public class UseCaseTest
    {
        [TestMethod]
        public void UseCase_Normal_Test()
        {
            Ruleset rules = Ruleset.FromDirectory(@"rules\valid", null);
            rules.AddDirectory(@"rules\custom", "my rules");

            RuleProcessor processor = new RuleProcessor(rules);            

            string lang = Language.FromFileName("testfilename.cpp");
            string testString = "strcpy(dest,src);";

            // strcpy test
            Issue[] issues = processor.Analyze(testString, lang);
            Assert.AreEqual(1, issues.Length, "strcpy should be flagged");
            Assert.AreEqual(0, issues[0].Index, "strcpy invalid index");
            Assert.AreEqual(16, issues[0].Length, "strcpy invalid length ");
            Assert.AreEqual("DS185832", issues[0].Rule.Id, "strcpy invalid rule");

            // Fix it test
            Assert.AreNotEqual(issues[0].Rule.Fixes.Length, 0, "strcpy invalid Fixes");
            CodeFix fix = issues[0].Rule.Fixes[0];
            string fixedCode = RuleProcessor.Fix(testString, fix);
            Assert.AreEqual("strcpy_s(dest, <size of dest>, src);", fixedCode, "strcpy invalid code fix");
            Assert.IsTrue(fix.Name.Contains("Change to strcpy_s"), "strcpy wrong fix name");

            // TODO test
            testString = "//TODO: fix this later";
            issues = processor.Analyze(testString, "csharp");
            Assert.AreEqual(1, issues.Length, "todo should be flagged");
            Assert.AreEqual(2, issues[0].Index, "todo invalid index");
            Assert.AreEqual(4, issues[0].Length, "todo invalid length ");
            Assert.AreEqual("DS176209", issues[0].Rule.Id, "todo invalid rule");
            Assert.AreEqual(0, issues[0].Rule.Fixes.Length, "todo invalid Fixes");
            Assert.AreEqual("my rules", issues[0].Rule.Tag, "todo invalid tag");

            // Same issue twice test
            testString = "MD5 hash = MD5.Create();";
            issues = processor.Analyze(testString, "csharp");
            Assert.AreEqual(2, issues.Length, "Same issue should be twice on line");
            Assert.AreEqual(issues[0].Rule, issues[1].Rule, "Same issues should have sames rule IDs");

            // Overlaping issues
            testString = "            MD5 hash = new MD5CryptoServiceProvider();";
            issues = processor.Analyze(testString, "csharp");
            Assert.AreEqual(2, issues.Length, "Overlaping issue count doesn't add up");

            // Override test
            testString = "strncat(dest, \"this is also bad\", strlen(dest))";
            issues = processor.Analyze(testString, new string[] { "c", "cpp" });
            Assert.AreEqual(2, issues.Length, "Override test failed");
        }

        [TestMethod]
        public void UseCase_IgnoreRules_Test()
        {
            Ruleset rules = Ruleset.FromDirectory(@"rules\valid", null);
            rules.AddDirectory(@"rules\custom", null);

            RuleProcessor processor = new RuleProcessor(rules);
            processor.EnableSuppressions = true;

            // MD5CryptoServiceProvider test
            string testString = "MD5 hash = new MD5CryptoServiceProvider(); //DevSkim: ignore DS126858";
            Issue[] issues = processor.Analyze(testString, "csharp");
            Assert.AreEqual(1, issues.Length, "MD5CryptoServiceProvider should be flagged");
            Assert.AreEqual(15, issues[0].Index, "MD5CryptoServiceProvider invalid index");
            Assert.AreEqual(24, issues[0].Length, "MD5CryptoServiceProvider invalid length ");
            Assert.AreEqual("DS168931", issues[0].Rule.Id, "MD5CryptoServiceProvider invalid rule");

            // Ignore until test
            DateTime expirationDate = DateTime.Now.AddDays(5);
            testString = "requests.get('somelink', verify = False) #DevSkim: ignore DS130821 until {0:yyyy}-{0:MM}-{0:dd}";
            issues = processor.Analyze(string.Format(testString, expirationDate), "python");
            Assert.AreEqual(0, issues.Length, "Ignore until should not be flagged");

            // Expired until test
            expirationDate = DateTime.Now;
            issues = processor.Analyze(string.Format(testString, expirationDate), "python");
            Assert.AreEqual(1, issues.Length, "Expired until should be flagged");

            // Ignore all until test
            expirationDate = DateTime.Now.AddDays(5);
            testString = "MD5 hash  = new MD5.Create(); #DevSkim: ignore all until {0:yyyy}-{0:MM}-{0:dd}";
            issues = processor.Analyze(string.Format(testString, expirationDate), "csharp");
            Assert.AreEqual(0, issues.Length, "Ignore all until should not be flagged");

            // Expired all test
            expirationDate = DateTime.Now;
            testString = "MD5 hash = new MD5CryptoServiceProvider(); //DevSkim: ignore all until {0:yyyy}-{0:MM}-{0:dd}";
            issues = processor.Analyze(string.Format(testString, expirationDate), "csharp");
            Assert.AreEqual(2, issues.Length, "Expired all should be flagged");
        }

        [TestMethod]
        public void UseCase_IgnoreSuppression_Test()
        {
            Ruleset rules = Ruleset.FromDirectory(@"rules\valid", null);
            rules.AddDirectory(@"rules\custom", null);

            RuleProcessor processor = new RuleProcessor(rules);
            processor.EnableSuppressions = false;

            // MD5CryptoServiceProvider test
            string testString = "MD5 hash = new MD5CryptoServiceProvider(); //DevSkim: ignore DS126858";

            Issue[] issues = processor.Analyze(testString, "csharp");
            Assert.AreEqual(2, issues.Length, "MD5CryptoServiceProvider should be flagged");
            Assert.AreEqual(0, issues[1].Index, "MD5CryptoServiceProvider invalid index");
            Assert.AreEqual(3, issues[1].Length, "MD5CryptoServiceProvider invalid length ");
            Assert.AreEqual("DS126858", issues[1].Rule.Id, "MD5CryptoServiceProvider invalid rule");
        }

        [TestMethod]
        public void UseCase_SuppressionExists_Test()
        {
            string testString = "MD5 hash = new MD5CryptoServiceProvider(); //DevSkim: ignore DS126858,DS168931 until {0:yyyy}-{0:MM}-{0:dd}";
            DateTime expirationDate = DateTime.Now.AddDays(5);

            Suppression sup = new Suppression(string.Format(testString, expirationDate));
            Assert.IsTrue(sup.IsIssueSuppressed("DS126858"), "Is suppressed DS126858 should be True");
            Assert.IsTrue(sup.IsIssueSuppressed("DS168931"), "Is suppressed DS168931 should be True");

            Assert.IsTrue(sup.IsInEffect, "Suppression should be in effect");
            Assert.AreEqual(45, sup.Index, "Suppression start index doesn't match");
            Assert.AreEqual(50, sup.Length, "Suppression length doesn't match");
            Assert.AreEqual(expirationDate.ToShortDateString(), sup.ExpirationDate.ToShortDateString(), "Suppression date doesn't match");

            string[] issues = sup.GetIssues();
            Assert.IsTrue(issues.Contains("DS126858"), "Issues list is missing DS126858");
            Assert.IsTrue(issues.Contains("DS168931"), "Issues list is missing DS168931");            
        }
    
        [TestMethod]
        public void UseCase_ManualReview_Test()
        {
            Ruleset rules = Ruleset.FromDirectory(@"rules\valid", null);
            rules.AddDirectory(@"rules\custom", null);

            RuleProcessor processor = new RuleProcessor(rules);
            string testString = "eval(something)";
            Issue[] issues = processor.Analyze(testString, "javascript");
            Assert.AreEqual(0, issues.Length, "Manual Review should not be flagged");

            processor.SeverityLevel |= Severity.ManualReview;
            issues = processor.Analyze(testString, "javascript");            
            Assert.AreEqual(1, issues.Length, "Manual Review should be flagged");
        }

        [TestMethod]
        public void LangugeSelectorTest()
        {
            Ruleset ruleset = Ruleset.FromDirectory(@"rules\valid", null);
            RuleProcessor processor = new RuleProcessor(ruleset);
            string testString = "<package id=\"Microsoft.IdentityModel.Tokens\" version=\"5.1.0\"";

            string lang = Language.FromFileName("helloworld.klingon");
            Assert.AreEqual(string.Empty, lang, "Klingon language should not be detected");

            lang = Language.FromFileName("project\\packages.config");
            Issue[] issues = processor.Analyze(testString, lang);
            Assert.AreEqual(1, issues.Length, "There should be positive hit");

            bool langExists = Language.GetNames().Contains("csharp");
            Assert.IsTrue(langExists, "csharp should be in the collection");

            langExists = Language.GetNames().Contains("klyngon");
            Assert.IsFalse(langExists, "klingon should not be in the collection");
        }

        [TestMethod]
        public void CommentingTest()
        { 
            string str = Language.GetCommentPrefix("python");
            Assert.AreEqual("#", str, "Python comment prefix doesn't match");
            str = Language.GetCommentSuffix("python");
            Assert.AreEqual(string.Empty, str, "Python comment suffix doesn't match");

            str = Language.GetCommentPrefix("klyngon");
            Assert.AreEqual(string.Empty, str, "Klyngon comment prefix doesn't match");
            str = Language.GetCommentSuffix("klyngon");
            Assert.AreEqual(string.Empty, str, "Klyngon comment suffix doesn't match");
        }
    }
}
