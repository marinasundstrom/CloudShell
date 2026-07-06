FROM python:3.12-slim

WORKDIR /src
COPY sdk/python/cloudshell ./sdk/python/cloudshell
COPY samples/PythonContainerApp/App ./samples/PythonContainerApp/App

ENV PYTHONPATH=/src/sdk/python/cloudshell
WORKDIR /src/samples/PythonContainerApp/App
EXPOSE 8080

CMD ["python3", "app.py"]
