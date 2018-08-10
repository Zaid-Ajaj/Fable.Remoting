namespace Fable.Remoting.Server 

open System 
open Newtonsoft.Json.Linq 
open System.Text

module DocsApp = 

    let append (input: string) (builder: StringBuilder) = 
        builder.Append(input)

    let documentedMethods (schema: JObject) = 
        let builder = StringBuilder()
        builder.Append("<ul class=\"list-group\">") |> ignore 
        for route in schema.["routes"] do
            let color = 
                match (route.["httpMethod"].Value<string>()) with  
                | "GET" -> "lightblue"
                | "POST" -> "lightgreen"
                | _ -> ""
            let routeName = route.["remoteFunction"].Value<string>()
            builder
            |> append (sprintf "<li class=\"list-group-item route-method\" data-route=\"%s\" style=\"padding:20px\">" routeName)
            |> append (sprintf "<span style=\"border: 1px %s solid; padding: 5px; border-radius: 5px; margin-right: 30px;\">" color)
            |> append (route.["httpMethod"].Value<string>())
            |> append "</span>"
            |> append (routeName)
            |> append "</li>" |> ignore 
        builder.Append("</ul>") |> ignore
        builder.ToString()


    let embedded (name: string) (url: string) (schema: JObject) = 
        let app = 
            """
            <!DOCTYPE html>
            <html> 
               <head>
                    <title>{AppTitle}</title>
                    <link rel="stylesheet" href="https://stackpath.bootstrapcdn.com/bootstrap/4.1.3/css/bootstrap.min.css" integrity="sha384-MCw98/SFnGE8fJT3GXwEOngsV7Zt27NXFoaoApmYm81iuXoPkFOJwJ8ERdknLPMO" crossorigin="anonymous">
                    <script src="https://code.jquery.com/jquery-3.3.1.min.js" crossorigin="anonymous"></script>
               </head>
               <body style="width:95%">
                    <style>
                        .list-group-item:hover {
                            background-color: rgba(0,0,0, 0.03);
                            cursor: pointer;
                        }
                    </style>

                    <div class="row">
                        <div class="col-md-4">
                            <div style="padding: 40px;">
                                <h1>{AppTitle}</h1>
                                {DocumentedMethods}
                            </div>
                        </div>
                        <div class="col-md-7">
                            <div class="card" style="padding:20px; margin-top:95px;">
                               <div class="card-body" id="content">
                                  
                               </div>
                            </div>
                        </div>
                    </div>

                    <script>
                        schema = {};

                        var findRoute = function(routeName) {
                            for(var i = 0; i < schema.routes.length; i++) {
                                if (schema.routes[i].remoteFunction === routeName) {
                                    return schema.routes[i];
                                }
                            }

                            return undefined;
                        };

                        $(function() { 
                            $.ajax("{SchemaUrl}/$schema", { 
                                method : 'OPTIONS', 
                                success : function(data) { 
                                    schema = JSON.parse(data);
                                }
                            }); 

                            var content = $("#content");

                            $(".route-method").click(function() {
                                var current = $(this);
                                var routeInfo = findRoute(current.attr("data-route"));
                                var color = routeInfo.httpMethod === "POST" ? "lightgreen" : "lightblue";
                                var httpMethod = "<span style='border: 1px " + color + " solid; padding: 5px; border-radius: 5px; margin-right: 10px;'>" + routeInfo.httpMethod + "</span>";
                                content.html("");
                                var header = $("<h2>" + routeInfo.alias + "</h2>");
                                content.append(header);
                                if (routeInfo.description !== "") {
                                    content.append("<div class='alert alert-success' style='font-size:18px; margin-top:30px;margin-bottom:30px'>" +  routeInfo.description + "</div>");
                                } else {
                                    content.append("<div class='alert alert-warning' style='font-size:18px; margin-top:30px; margin-bottom:30px'> No description available </div>");                                    
                                }

                                content.append("<p style='font-size:18px;'>" + httpMethod + window.location.href.replace(window.location.pathname, routeInfo.route) + "</p>");

                                var runBtn = $("<div class='btn btn-success'>Run</div>");

                                

                                if (routeInfo.httpMethod === "GET") {
                                    content.append(runBtn);
                                    
                                    runBtn.click(function() {
                                        $("#output").remove(); 

                                        $.ajax(routeInfo.route, {
                                            error: function(response) {
                                                content.append("<div style='margin-top:20px;' id='output' class='alert alert-info' >" + JSON.stringify(response.responseJSON, null, 2) + "</div>");
                                            }, 

                                            success: function(data) {
                                                content.append("<div style='margin-top:20px;' id='output' class='alert alert-info' >" + JSON.stringify(data, null, 2) + "</div>");
                                            }
                                        })
                                    })
                                }                            
                            });
                        });
                    </script>
               </body>
            </html>
            """
        // Poor man's view engine
        app.Replace("\r\n", "")
           .Replace("{AppTitle}", name)
           .Replace("{SchemaUrl}", url)
           .Replace("{DocumentedMethods}", documentedMethods schema)