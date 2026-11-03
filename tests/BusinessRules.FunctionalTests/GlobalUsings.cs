global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Net.Http;
global using System.Security.Claims;
global using System.Text.Json;
global using System.Threading.Tasks;
global using eShop.BusinessRules.Client.Rules;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.VisualStudio.TestTools.UnitTesting;

// The tests share one database and assert on audit rows, so they run sequentially.
[assembly: DoNotParallelize]
