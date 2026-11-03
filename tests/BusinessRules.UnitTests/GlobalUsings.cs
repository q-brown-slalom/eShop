global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text.Json;
global using eShop.BusinessRules.Client.Pricing;
global using eShop.BusinessRules.Client.Rules;
global using eShop.BusinessRules.Client.Snapshot;
global using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
