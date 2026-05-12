using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace backend.main.application.bootstrap
{
    public static class RoutePaths
    {
        public const string ApiPrefix = "api";
        public const string AuthPrefix = "auth";
        public const string ApiAuthPath = "/" + ApiPrefix + "/" + AuthPrefix;
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
