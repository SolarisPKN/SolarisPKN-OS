.PHONY: test package check
test:
	python3 -m unittest discover -s tests -p 'test_*.py' -v
package:
	bash packaging/build-deb.sh
check: test package
