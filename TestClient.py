import requests

#make a request against http://localhost:8922/Communication/backendcluster?partitionId=0

r = requests.get('http://localhost:8922/Communication/backendcluster?partitionId=0')
print(r.content)

#make POST a request against http://localhost:8922/Communication/processpurchase
# with body [{"id": 0,"name": "Pan","description": "string","category": "Alimentos","quantity": 1}]

#r = requests.post('http://localhost:8922/Communication/processpurchase', json=[{"id": 0,"name": "Pan","description": "string","category": "Alimentos","quantity": 1}])
#print(r.content)

# make the same request ten time in parallel
import threading

def make_request():
    r = requests.post('http://localhost:8922/Communication/processpurchase', 
                      json=[
                          {"id": 0,"name": "Pan","description": "string","category": "Alimentos","quantity": 30},
                          {"id": 1,"name": "CocaCola","description": "string","category": "Bebidas","quantity": 0},
                          {"id": 2,"name": "Notebook","description": "string","category": "Tecno","quantity": 0}
                        ]
                    )
    print(r.content)

threads = []
for i in range(10):
    t = threading.Thread(target=make_request)
    threads.append(t)
    t.start()

for t in threads:
    t.join()

