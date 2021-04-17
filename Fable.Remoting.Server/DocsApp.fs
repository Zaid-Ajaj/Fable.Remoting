namespace Fable.Remoting.Server

open System
open Newtonsoft.Json.Linq
open System.Text

module DocsApp =

    let append (input: string) (builder: StringBuilder) =
        builder.Append(input)

    let embedded (name: string) (url: string) (schema: JObject) =
        let app =
            """
            <!DOCTYPE html>
            <html lang="en">

            <head>
                <meta charset="UTF-8">
                <meta http-equiv="X-UA-Compatible" content="IE=edge">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{AppTitle}</title>
                <style>
                    * {
                        margin: 0;
                        padding: 0;
                        box-sizing: border-box;
                    }

                    body {
                        background-color: whitesmoke;
                        font-family: "Open Sans", sans-serif;
                    }

                    hr {
                        border: 0;
                        border-top: 1px solid rgba(0, 0, 0, .1);
                    }

                    button {
                        padding: 5px;
                        background: transparent;
                        box-shadow: 0 1px 2px rgb(0 0 0 / 10%);
                        color: #3b4151;
                        width: 110px;
                        cursor: pointer;
                    }

                    textarea {
                        padding: 6px;
                    }

                    select {
                        padding: 6px;
                    }

                    .container {
                        padding: 40px;
                    }

                    .bordered {
                        border: 1px solid gray;
                        border-radius: 3px;
                    }

                    #api-name {
                        font-weight: 600;
                        margin-bottom: 20px;
                        font-size: xx-large;
                    }

                    .get {
                        background-color: #48c774;
                        border-color: #48c774;
                        color: #fff;
                    }

                    .post {
                        background-color: #3298dc;
                        border-color: #3298dc;
                        color: #fff;
                    }

                    .route-header {
                        margin: 20px 0;
                        padding: 0 0 5px 0;
                        display: flex;
                        cursor: pointer;
                    }

                    .route-method {
                        padding: 5px;
                        width: 60px;
                        text-align: center;
                        font-weight: 600;
                    }

                    .route-url {
                        padding: 5px;
                        padding-left: 10px !important;
                        width: 100%;
                        font-weight: 600;
                    }

                    .route-examples {
                        padding: 20px;
                        border: 1px solid gray;
                        border-radius: 3px;
                        background-color: white;
                        display: flex;
                        flex-direction: column;
                    }

                    .route-examples>* {
                        margin-top: 5px;
                        margin-bottom: 5px;
                    }

                    .route-description {
                        color: rgb(74, 74, 74);
                    }

                    .hidden {
                        display: none !important;
                    }

                    .success {
                        color: rgb(29, 129, 39);
                        border-color: rgb(29, 129, 39);
                    }

                    .error {
                        color: rgb(212, 31, 28);
                        border-color: rgb(212, 31, 28);
                    }
                </style>
            </head>

            <body>
                <div class="container">
                    <div id="api-name"></div>
                    <div id="routes-container"></div>
                </div>

                <script>
                    function ready(callbackFunc) {
                        if (document.readyState !== 'loading') {
                            // Document is already ready, call the callback directly
                            callbackFunc();
                        } else if (document.addEventListener) {
                            // All modern browsers to register DOMContentLoaded
                            document.addEventListener('DOMContentLoaded', callbackFunc);
                        } else {
                            // Old IE browsers
                            document.attachEvent('onreadystatechange', function () {
                                if (document.readyState === 'complete') {
                                    callbackFunc();
                                }
                            });
                        }
                    }

                    function toJson(payload) {
                        return JSON.stringify(payload, null, 2)
                    }

                    function fromJson(payload) {
                        return JSON.parse(payload);
                    }

                    function buildDocsUI(schema) {
                        const apiName = document.querySelector('#api-name');
                        if (apiName) {
                            apiName.innerText = schema.name;
                            document.title = schema.name + ' Documentation';
                        }

                        const routesContainer = document.querySelector('#routes-container');
                        if (!routesContainer) { return; }

                        schema.routes.forEach(function (metadata) {
                            const routeContainer = document.createElement('div');

                            const routeHeader = document.createElement('div');
                            const routeMethod = document.createElement('div');
                            const routeUrl = document.createElement('div');

                            const routeExampleContainer = document.createElement('div');
                            const routeAlias = document.createElement('div');
                            const routeDescription = document.createElement('div');
                            const divider = document.createElement('hr');
                            const exampleDescription = document.createElement('div');
                            const exampleInput = document.createElement('textarea');
                            const tryItOut = document.createElement('button');
                            const exampleOutput = document.createElement('textarea');
                            const exampleDropdown = document.createElement('select');

                            routeMethod.innerText = metadata.httpMethod;
                            routeUrl.innerText = metadata.route;
                            routeAlias.innerText = metadata.alias ?? metadata.remoteFunction;
                            routeDescription.innerText = metadata.description;
                            exampleDescription.innerText = 'Request Body (function arguments as JSON array)';
                            exampleInput.rows = 5;
                            tryItOut.innerText = 'Try it out';
                            exampleOutput.readOnly = true;
                            exampleOutput.rows = 5;

                            routeHeader.classList.add('route-header');
                            routeMethod.classList.add('route-method', 'bordered', metadata.httpMethod === 'POST' ? 'post' : 'get');
                            routeUrl.classList.add('route-url');
                            routeDescription.classList.add('route-description');
                            tryItOut.classList.add('bordered');

                            routeExampleContainer.classList.add('route-examples', 'hidden');

                            routeHeader.appendChild(routeMethod);
                            routeHeader.appendChild(routeUrl);
                            routeExampleContainer.appendChild(routeAlias);

                            if (metadata.description) {
                                routeExampleContainer.appendChild(routeDescription);
                            }

                            // routeExampleContainer.appendChild(divider);

                            if (metadata.httpMethod === 'POST') {
                                routeExampleContainer.appendChild(exampleDescription);

                                const defaultOption = document.createElement('option');

                                defaultOption.innerText = 'Select example';
                                defaultOption.value = 0;

                                exampleDropdown.appendChild(defaultOption);

                                metadata.examples.forEach(function (example, index) {
                                    const value = index + 1;
                                    const exampleOption = document.createElement('option');

                                    exampleOption.innerText = example.description || 'Example ' + value.toString();
                                    exampleOption.value = value;

                                    exampleDropdown.appendChild(exampleOption);
                                });

                                if (metadata.examples.length) {
                                    routeExampleContainer.appendChild(exampleDropdown);
                                }

                                routeExampleContainer.appendChild(exampleInput);
                            }

                            routeExampleContainer.appendChild(tryItOut);
                            routeExampleContainer.appendChild(exampleOutput);

                            routeContainer.appendChild(routeHeader);
                            routeContainer.appendChild(routeExampleContainer);

                            routesContainer.appendChild(routeContainer);

                            exampleDropdown.addEventListener('change', function () {
                                exampleInput.value = null;

                                const value = parseInt(exampleDropdown.value);
                                if (value === 0) { return; }
                                if (!metadata.examples.length) { return; }

                                const example = metadata.examples[value - 1];
                                if (!example) { return; }

                                exampleInput.value = toJson(example.arguments);
                            });

                            tryItOut.addEventListener('click', function () {
                                if (metadata.httpMethod === 'POST' && !exampleInput.value) { return; }

                                const fetchRequest =
                                    metadata.httpMethod === 'POST' ?
                                        fetch(metadata.route, {
                                            method: metadata.httpMethod,
                                            body: exampleInput.value
                                        }) :
                                        fetch(metadata.route, {
                                            method: metadata.httpMethod
                                        });

                                fetchRequest.then(function (response) {
                                    const resultClass = response.ok ? 'success' : 'error';
                                    response.json().then(function (result) {
                                        exampleOutput.value = toJson(result);
                                        exampleOutput.classList.remove('success');
                                        exampleOutput.classList.remove('error');
                                        exampleOutput.classList.add(resultClass);
                                    })
                                });
                            });

                            routeHeader.addEventListener('click', function () {
                                if (routeExampleContainer.classList.contains('hidden')) {
                                    routeExampleContainer.classList.remove('hidden');
                                } else {
                                    routeExampleContainer.classList.add('hidden');
                                }
                            });
                        });
                    }

                    ready(function () {
                        fetch('{SchemaUrl}/$schema', {
                            method: 'OPTIONS'
                        }).then(function (response) {
                            if (response.ok) {
                                response.json().then(function (schema) { buildDocsUI(schema); });
                            } else {
                                alert('Failed to get the API documentation schema');
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