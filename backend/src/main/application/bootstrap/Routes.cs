using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace backend.main.application.bootstrap
{
    public static class RoutePaths
    {
        public const string ApiPrefix = "api";
        public const string AuthPrefix = "auth";
        public const string ApiAuthPath = "/" + ApiPrefix + "/" + AuthPrefix;

        /// <summary>
        /// Root for SignalR hubs. Spelled out with the api prefix because
        /// <see cref="RoutePrefixConvention"/> only rewrites MVC controllers, and because the
        /// SSR proxy only forwards paths under <c>/api</c>.
        /// </summary>
        public const string ApiHubsPath = "/" + ApiPrefix + "/hubs";

        /// <summary>Path of the club realtime hub (comments, presence, typing).</summary>
        public const string ClubRealtimeHubPath = ApiHubsPath + "/clubs";
    }

    public class RoutePrefixConvention : IApplicationModelConvention
    {
        private readonly AttributeRouteModel _routePrefix;

        public RoutePrefixConvention(string prefix)
        {
            _routePrefix = new AttributeRouteModel(new Microsoft.AspNetCore.Mvc.RouteAttribute(prefix));
        }

        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
            {
                foreach (var selector in controller.Selectors.Where(s => s.AttributeRouteModel != null))
                {
                    selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(_routePrefix, selector.AttributeRouteModel);
                }
            }
        }
    }
}
