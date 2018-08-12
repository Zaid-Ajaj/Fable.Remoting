namespace Fable.Remoting.Server 

open System 
open Newtonsoft.Json.Linq 
open System.Text

module DocsApp = 

    let append (input: string) (builder: StringBuilder) = 
        builder.Append(input)

    let documentedMethods (schema: JObject) name = 
        let builder = StringBuilder()
        builder.Append("<div style='height:90%; overflow-y: auto; padding: 30px;position: fixed;'>") |> ignore
        builder.Append("<h1 style='margin-bottom:20px;'>" + name  + "</h1>") |> ignore
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
        builder.Append("</div>") |> ignore
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
                        /* width */
                        ::-webkit-scrollbar {
                            width: 7px;
                        }

                        /* Track */
                        ::-webkit-scrollbar-track {
                            background: #f1f1f1; 
                        }
                         
                        /* Handle */
                        ::-webkit-scrollbar-thumb {
                            background: #888; 
                        }

                        /* Handle on hover */
                        ::-webkit-scrollbar-thumb:hover {
                            background: #555; 
                        }

                        .list-group-item:hover {
                            background-color: rgba(0,0,0, 0.03);
                            cursor: pointer;
                        }
                    </style>

                    <div class="row">
                        <div class="col-md-4">
                            <div style="padding: 40px;">
                                {DocumentedMethods}
                            </div>
                        </div>
                        <div class="col-md-8">
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
                                    content.append("<br />");

                                    runBtn.click(function() {
                                        $("#output").remove(); 

                                        $.ajax(routeInfo.route, {
                                            error: function(response) {
                                                var outputData = JSON.stringify(response.responseJSON, null, 2);
                                                var textarea = $("<textarea id='output' class='form-control' style='margin-top:20px; font-size:20px' rows='5' ></textarea>");
                                                textarea.val(outputData);
                                                content.append(textarea);
                                            }, 

                                            success: function(data) {
                                                var outputData =  JSON.stringify(data, null, 2);
                                                var textarea = $("<textarea id='output' class='form-control' style='margin-top:20px; font-size:20px' rows='5' ></textarea>");
                                                textarea.val(outputData);
                                                content.append(textarea);
                                            }
                                        });
                                    });
                                } else {
                                    content.append("<h5>Request Body (function arguments as JSON array)</h5>");

                                    if (routeInfo.examples.length > 0) {
                                        content.append("<hr />");
                                        var group = $("<div class='button-group' role='group'></div>");
                                        group.append($("<span style='font-size:18px;'>Examples: </span>"));
                                        for(var i = 0; i < routeInfo.examples.length; i++) {
                                            var exampleArguments = routeInfo.examples[i].arguments;
                                            var example = $("<button data-example='" + (i+1) + "' data-func='" + routeInfo.remoteFunction +  "' class='btn btn-info' style='margin:5px'>" +  (i+1) + "</button>");
                                            example.click(function() {
                                                var currentExample = $(this);
                                                var currentFunc = currentExample.attr("data-func");
                                                var exampleIndex = parseInt(currentExample.attr("data-example"));
                                                $("#inputJson").val(JSON.stringify(routeInfo.examples[exampleIndex - 1].arguments, null, 2));
                                            });
                                            
                                            group.append(example);
                                        }

                                        content.append(group);
                                    }

                                    content.append("<textarea id='inputJson' class='form-control' style='margin-top:20px; font-size:20px;' rows='4' ></textarea>");
                                    content.append("<br />");
                                    content.append(runBtn);
                                    content.append("<br />");
                                    
                                    runBtn.click(function() {
                                        $("#output").remove(); 

                                        $.ajax({
                                            url: routeInfo.route,
                                            method: 'POST',
                                            data: $("#inputJson").val(),
                                            error: function(response) {
                                                var outputData = JSON.stringify(JSON.parse(response.responseText), null, 2);
                                                console.log("error at", routeInfo.route, "\n", outputData, response);
                                                var textarea = $("<textarea id='output' class='form-control' style='margin-top:20px; font-size:20px; color:red' rows='5' ></textarea>");
                                                textarea.val(outputData);
                                                content.append(textarea);
                                            }, 

                                            success: function(data) {
                                                var outputData =  JSON.stringify(data, null, 2);
                                                var textarea = $("<textarea id='output' class='form-control' style='margin-top:20px; color:green'font-size:20px;' rows='5' ></textarea>");
                                                textarea.val(outputData);
                                                content.append(textarea);
                                            }
                                        });
                                    });

                                    if (routeInfo.examples.length > 0) {
                                        $("#inputJson").val(JSON.stringify(routeInfo.examples[0].arguments, null, 2));
                                    }
                                }                            
                            });
                        });
                    </script>
               </body>
            </html>
            """
        // Poor man's view engine
        app.Replace("\r\n", "\n")
           .Replace("{AppTitle}", name)
           .Replace("{SchemaUrl}", url)
           .Replace("{DocumentedMethods}", documentedMethods schema name)